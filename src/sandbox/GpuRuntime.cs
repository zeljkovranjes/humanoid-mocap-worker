using HumanoidMocapWorker.Vulkan;

namespace HumanoidMocapWorker;

/// <summary>Backs the static s&amp;box graphics API with one Vulkan device. Work recorded
/// outside a tensor batch goes to an immediate recorder that is submitted before the next
/// batch, or sooner when its staging memory grows large. Main thread only.</summary>
internal sealed class GpuRuntime : IDisposable
{
    // Bounds host staging memory while large checkpoints are uploaded.
    const ulong ImmediateStagingLimit = 256UL << 20;
    readonly CommandRecorder immediate;
    readonly ShaderTable table;
    readonly Dictionary<string, ShaderProgram> programs = new(StringComparer.Ordinal);
    readonly List<CommandRecorder> batches = new();

    /// <summary>The runtime the Sandbox shim uses, or null before <see cref="Start"/>.</summary>
    internal static GpuRuntime Current { get; private set; }
    internal GpuDevice Device { get; }
    /// <summary>The tensor batch being recorded, or null.</summary>
    internal CommandRecorder Active { get; set; }

    GpuRuntime(GpuDevice device, ShaderTable table)
    {
        Device = device; this.table = table;
        immediate = new CommandRecorder(device);
    }

    /// <summary>Opens the GPU and installs it as <see cref="Current"/>. Call on the main thread.</summary>
    internal static GpuRuntime Start()
    {
        if (Current is not null) throw new InvalidOperationException("The GPU runtime is already started.");
        var table = ShaderTable.Load();
        var device = GpuDevice.Create();
        try { return Current = new GpuRuntime(device, table); }
        catch { device.Dispose(); throw; }
    }

    internal static GpuRuntime Require() => Current ?? throw new InvalidOperationException("The GPU runtime has not been started.");

    /// <summary>The batch being recorded, else the immediate recorder (started if idle).</summary>
    internal CommandRecorder Recorder
    {
        get
        {
            if (Active is not null) return Active;
            if (!immediate.IsRecording) immediate.Begin();
            return immediate;
        }
    }

    /// <summary>Submits immediate work early once its staging copies exceed the limit.</summary>
    internal void LimitStaging()
    {
        if (Active is not null || !immediate.IsRecording || immediate.StagedBytes < ImmediateStagingLimit) return;
        immediate.Submit(); immediate.Wait();
    }

    /// <summary>Submits recorded immediate work without waiting, so a batch that follows sees it.</summary>
    internal void FlushImmediate()
    {
        if (immediate.IsRecording) immediate.Submit();
    }

    /// <summary>Blocks until every submitted command buffer has completed.</summary>
    internal void WaitIdle()
    {
        if (Active is not null) throw new InvalidOperationException("GPU resources cannot be released while a tensor batch is recording.");
        FlushImmediate(); immediate.Wait();
        foreach (var batch in batches) batch.Wait();
    }

    /// <summary>Destroys a buffer after all GPU work that might use it has finished.</summary>
    internal void Release(DeviceBuffer buffer)
    {
        WaitIdle();
        buffer.Dispose();
    }

    internal CommandRecorder CreateBatch()
    {
        var recorder = new CommandRecorder(Device);
        batches.Add(recorder);
        return recorder;
    }

    internal void ReleaseBatch(CommandRecorder recorder)
    {
        batches.Remove(recorder);
        recorder.Dispose();
    }

    /// <summary>The pipeline for a shader path as passed to ComputeShader. Throws when
    /// the embedded table has no such shader.</summary>
    internal ShaderProgram Program(string path)
    {
        if (programs.TryGetValue(path, out var program)) return program;
        var entry = Array.Find(table.Shaders, shader => string.Equals(shader.Path, path, StringComparison.Ordinal))
            ?? throw new ArgumentException($"No extracted SPIR-V for compute shader '{path}'. Regenerate src/gen with codegen.", nameof(path));
        program = new ShaderProgram(Device, entry, ShaderTable.Spirv(entry));
        programs.Add(path, program);
        return program;
    }

    /// <summary>Waits for the GPU, then releases pipelines, recorders and the device.</summary>
    public void Dispose()
    {
        if (Current != this) return;
        try { WaitIdle(); }
        finally
        {
            foreach (var program in programs.Values) program.Dispose();
            foreach (var batch in batches) batch.Dispose();
            immediate.Dispose(); Device.Dispose();
            Current = null;
        }
    }
}
