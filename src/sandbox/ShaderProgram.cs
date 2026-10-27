using HumanoidMocapWorker.Vulkan;

namespace HumanoidMocapWorker;

/// <summary>A compiled compute pipeline plus the attribute layout the editor binds by name.</summary>
internal sealed class ShaderProgram : IDisposable
{
    // Vulkan guarantees at least this many workgroups per dimension.
    const int MaximumGroups = 65535;
    internal ShaderEntry Entry { get; }
    internal ComputePipeline Pipeline { get; }

    internal ShaderProgram(GpuDevice device, ShaderEntry entry, byte[] spirv)
    {
        if (entry.LocalSize is not { Length: 3 } || entry.LocalSize.Any(size => size < 1))
            throw new InvalidDataException($"Shader '{entry.Path}' has an invalid thread-group size.");
        Entry = entry;
        var bindings = new (uint, uint)[entry.Buffers.Length + (entry.Constants is null ? 0 : 1)];
        for (var index = 0; index < entry.Buffers.Length; index++) bindings[index] = (entry.Buffers[index].Binding, Vk.DescriptorStorageBuffer);
        if (entry.Constants is not null) bindings[^1] = (entry.Constants.Binding, Vk.DescriptorUniformBuffer);
        Pipeline = new ComputePipeline(device, spirv, bindings);
    }

    /// <summary>Workgroups covering a thread count on one axis, as s&amp;box's Dispatch rounds
    /// thread counts up to whole groups. Throws when the count exceeds the dispatch limit.</summary>
    internal uint Groups(int threads, int axis)
    {
        if (threads < 1) throw new ArgumentException("Thread counts cannot be less than 1.", nameof(threads));
        var groups = (threads + (long)Entry.LocalSize[axis] - 1) / Entry.LocalSize[axis];
        if (groups > MaximumGroups) throw new ArgumentException($"Dispatch of {threads} threads exceeds {MaximumGroups} groups for '{Entry.Path}'.");
        return (uint)groups;
    }

    public void Dispose() => Pipeline.Dispose();
}
