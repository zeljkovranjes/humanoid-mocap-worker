namespace HumanoidMocapWorker.Vulkan;

/// <summary>Records one command buffer at a time and submits it with a fence. Every
/// command is followed by a full compute/transfer barrier, so recorded work runs in order.
/// Begin waits for the previous submission. Use from one thread.</summary>
internal sealed unsafe class CommandRecorder : IDisposable
{
    const ulong UniformAlignment = 256; // the largest minUniformBufferOffsetAlignment Vulkan allows
    const ulong UniformChunkBytes = 1 << 20, StagingChunkBytes = 64 << 20;
    const uint WorkStages = Vk.PipelineStageComputeShader | Vk.PipelineStageTransfer;
    const uint WorkWrites = Vk.AccessShaderWrite | Vk.AccessTransferWrite;
    const uint WorkAccess = Vk.AccessUniformRead | Vk.AccessShaderRead | Vk.AccessShaderWrite | Vk.AccessTransferRead | Vk.AccessTransferWrite;
    readonly GpuDevice device;
    readonly ulong pool, fence;
    readonly nint commands;
    readonly HostArena uniforms, staging;
    DeviceBuffer readback;
    bool disposed;

    internal bool IsRecording { get; private set; }
    internal bool IsSubmitted { get; private set; }
    /// <summary>Staging bytes recorded since Begin; the caller may submit early to bound host memory.</summary>
    internal ulong StagedBytes => staging.Used;

    internal CommandRecorder(GpuDevice device)
    {
        this.device = device;
        uniforms = new(device, UniformChunkBytes, Vk.BufferUniform);
        staging = new(device, StagingChunkBytes, Vk.BufferTransferSource);
        try
        {
            Vk.CommandPoolCreateInfo poolInfo = default;
            poolInfo.SType = Vk.StructureCommandPoolCreateInfo; poolInfo.Flags = Vk.CommandPoolResetBuffer; poolInfo.QueueFamilyIndex = device.QueueFamily;
            ulong createdPool;
            Vk.Check(Vk.CreateCommandPool(device.Handle, &poolInfo, null, &createdPool), "vkCreateCommandPool");
            pool = createdPool;
            Vk.CommandBufferAllocateInfo allocate = default;
            allocate.SType = Vk.StructureCommandBufferAllocateInfo; allocate.CommandPool = pool; allocate.CommandBufferCount = 1;
            nint buffer;
            Vk.Check(Vk.AllocateCommandBuffers(device.Handle, &allocate, &buffer), "vkAllocateCommandBuffers");
            commands = buffer;
            Vk.FenceCreateInfo fenceInfo = default; fenceInfo.SType = Vk.StructureFenceCreateInfo;
            ulong createdFence;
            Vk.Check(Vk.CreateFence(device.Handle, &fenceInfo, null, &createdFence), "vkCreateFence");
            fence = createdFence;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Starts a new command buffer, first waiting for this recorder's previous submission.
    /// The opening barrier orders it after everything submitted earlier on the queue.</summary>
    internal void Begin()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRecording) throw new InvalidOperationException("This command recorder is already recording.");
        Wait();
        uniforms.Reset(); staging.Reset();
        var fenceHandle = fence;
        Vk.Check(Vk.ResetFences(device.Handle, 1, &fenceHandle), "vkResetFences");
        Vk.CommandBufferBeginInfo info = default;
        info.SType = Vk.StructureCommandBufferBeginInfo; info.Flags = Vk.CommandBufferOneTimeSubmit;
        Vk.Check(Vk.BeginCommandBuffer(commands, &info), "vkBeginCommandBuffer");
        IsRecording = true;
        Barrier(Vk.PipelineStageAllCommands, Vk.AccessMemoryWrite, Vk.PipelineStageAllCommands, Vk.AccessMemoryRead | Vk.AccessMemoryWrite);
    }

    /// <summary>Records a dispatch. Constants go to a fresh uniform slice at their binding;
    /// storage buffers are bound whole.</summary>
    internal void Dispatch(ComputePipeline pipeline, uint groupsX, uint groupsY, uint groupsZ,
        uint constantsBinding, ReadOnlySpan<byte> constants, ReadOnlySpan<(uint Binding, DeviceBuffer Buffer)> storage)
    {
        RequireRecording();
        var count = storage.Length + (constants.IsEmpty ? 0 : 1);
        var writes = stackalloc Vk.WriteDescriptorSet[count];
        var infos = stackalloc Vk.DescriptorBufferInfo[count];
        for (var index = 0; index < storage.Length; index++)
        {
            infos[index] = new Vk.DescriptorBufferInfo { Buffer = storage[index].Buffer.Handle, Range = Vk.WholeSize };
            writes[index] = Write(storage[index].Binding, Vk.DescriptorStorageBuffer, infos + index);
        }
        if (!constants.IsEmpty)
        {
            var (chunk, offset, _) = uniforms.Allocate((ulong)constants.Length, UniformAlignment);
            constants.CopyTo(new Span<byte>(chunk.Mapped + offset, constants.Length));
            infos[storage.Length] = new Vk.DescriptorBufferInfo { Buffer = chunk.Handle, Offset = offset, Range = (ulong)constants.Length };
            writes[storage.Length] = Write(constantsBinding, Vk.DescriptorUniformBuffer, infos + storage.Length);
        }
        Vk.CmdBindPipeline(commands, Vk.BindPointCompute, pipeline.Handle);
        device.PushDescriptorSet(commands, Vk.BindPointCompute, pipeline.Layout, 0, (uint)count, writes);
        Vk.CmdDispatch(commands, groupsX, groupsY, groupsZ);
        WorkBarrier();
    }

    internal void Copy(DeviceBuffer source, ulong sourceOffset, DeviceBuffer destination, ulong destinationOffset, ulong bytes)
    {
        RequireRecording();
        var region = new Vk.BufferCopy { SourceOffset = sourceOffset, DestinationOffset = destinationOffset, Size = bytes };
        Vk.CmdCopyBuffer(commands, source.Handle, destination.Handle, 1, &region);
        WorkBarrier();
    }

    internal void Fill(DeviceBuffer destination, uint value)
    {
        RequireRecording();
        Vk.CmdFillBuffer(commands, destination.Handle, 0, Vk.WholeSize, value);
        WorkBarrier();
    }

    /// <summary>Copies data into staging now and records its transfer to destination.</summary>
    internal void Upload(DeviceBuffer destination, ulong destinationOffset, ReadOnlySpan<byte> data)
    {
        RequireRecording();
        while (!data.IsEmpty)
        {
            var (chunk, offset, length) = staging.Allocate((ulong)data.Length, 16);
            data[..(int)length].CopyTo(new Span<byte>(chunk.Mapped + offset, (int)length));
            var region = new Vk.BufferCopy { SourceOffset = offset, DestinationOffset = destinationOffset, Size = length };
            Vk.CmdCopyBuffer(commands, chunk.Handle, destination.Handle, 1, &region);
            data = data[(int)length..]; destinationOffset += length;
        }
        WorkBarrier();
    }

    /// <summary>Records a copy of source's first bytes into host memory, readable after Wait.</summary>
    internal void ReadBack(DeviceBuffer source, ulong bytes)
    {
        RequireRecording();
        if (readback is null || readback.Size < bytes)
        {
            readback?.Dispose(); readback = null;
            readback = device.CreateBuffer(bytes, Vk.BufferTransferDestination, MemoryKind.Readback);
        }
        var region = new Vk.BufferCopy { Size = bytes };
        Vk.CmdCopyBuffer(commands, source.Handle, readback.Handle, 1, &region);
        Barrier(Vk.PipelineStageTransfer, Vk.AccessTransferWrite, Vk.PipelineStageHost, Vk.AccessHostRead);
    }

    /// <summary>The bytes recorded by the last ReadBack. Valid after the submission completes.</summary>
    internal ReadOnlySpan<byte> ReadBackData(int bytes)
    {
        if (IsSubmitted || readback is null || (ulong)bytes > readback.Size) throw new InvalidOperationException("No completed readback is available.");
        return new ReadOnlySpan<byte>(readback.Mapped, bytes);
    }

    internal void Submit()
    {
        RequireRecording();
        IsRecording = false;
        Vk.Check(Vk.EndCommandBuffer(commands), "vkEndCommandBuffer");
        var buffer = commands;
        Vk.SubmitInfo info = default;
        info.SType = Vk.StructureSubmitInfo; info.CommandBufferCount = 1; info.CommandBuffers = &buffer;
        Vk.Check(Vk.QueueSubmit(device.Queue, 1, &info, fence), "vkQueueSubmit");
        IsSubmitted = true;
    }

    /// <summary>Blocks until the last submission completes. Safe to call from any thread
    /// while the owner does not record; call <see cref="Complete"/> on the owner thread after.</summary>
    internal void WaitForFence()
    {
        var fenceHandle = fence;
        Vk.Check(Vk.WaitForFences(device.Handle, 1, &fenceHandle, 1, ulong.MaxValue), "vkWaitForFences");
    }

    internal void Complete() => IsSubmitted = false;

    /// <summary>Blocks until any submitted work finishes.</summary>
    internal void Wait()
    {
        if (!IsSubmitted) return;
        WaitForFence(); Complete();
    }

    void RequireRecording()
    {
        if (!IsRecording) throw new InvalidOperationException("Begin recording before adding GPU commands.");
    }

    void WorkBarrier() => Barrier(WorkStages, WorkWrites, WorkStages, WorkAccess);

    void Barrier(uint sourceStages, uint sourceAccess, uint destinationStages, uint destinationAccess)
    {
        var barrier = new Vk.MemoryBarrier { SType = Vk.StructureMemoryBarrier, SourceAccess = sourceAccess, DestinationAccess = destinationAccess };
        Vk.CmdPipelineBarrier(commands, sourceStages, destinationStages, 0, 1, &barrier, 0, null, 0, null);
    }

    static Vk.WriteDescriptorSet Write(uint binding, uint type, Vk.DescriptorBufferInfo* info) => new()
    {
        SType = Vk.StructureWriteDescriptorSet, Binding = binding, DescriptorCount = 1, DescriptorType = type, BufferInfo = info,
    };

    /// <summary>Waits for submitted work, then releases the command pool, fence and arenas.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (IsSubmitted) Wait();
        if (fence != 0) Vk.DestroyFence(device.Handle, fence, null);
        if (pool != 0) Vk.DestroyCommandPool(device.Handle, pool, null);
        readback?.Dispose(); uniforms.Dispose(); staging.Dispose();
    }
}
