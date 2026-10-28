using HumanoidMocapWorker;
using HumanoidMocapWorker.Vulkan;
using Sandbox;

namespace HumanoidMocapX.TensorInference;

/// <summary>Replaces Humanoid Mocap X's TensorBatch, which needs an engine scene and camera.
/// Same contract: records tensor work, submits it and reads back the result prefix.
/// Create, run and dispose on the main thread. One batch may be in flight per instance.</summary>
public sealed class TensorBatch : IDisposable
{
    readonly CommandRecorder recorder;
    bool running, disposed;

    /// <summary>Throws when the GPU runtime is unavailable.</summary>
    public TensorBatch()
    {
        ThreadSafe.AssertIsMainThread();
        if (!Graphics.IsAvailable) throw new InvalidOperationException("Tensor inference requires an initialized graphics device.");
        recorder = GpuRuntime.Require().CreateBatch();
    }

    /// <summary>Runs commands on the GPU, then copies the result prefix into destination.
    /// Keep buffers alive until completion. Cancellation after submission waits for the
    /// GPU before throwing; recording failures also propagate.</summary>
    public async Task RunAsync(Action commands, GpuBuffer<float> result, Memory<float> destination,
        CancellationToken cancellation = default)
    {
        ThreadSafe.AssertIsMainThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        if (running) throw new InvalidOperationException("Await the current tensor batch before starting another.");
        if (result is null || !result.IsValid || destination.Length < 1 || destination.Length > result.ElementCount)
            throw new ArgumentException("Readback requires a valid buffer and a nonempty destination within its bounds.");
        cancellation.ThrowIfCancellationRequested();
        var runtime = GpuRuntime.Require();
        var bytes = destination.Length * sizeof(float);
        Exception recordingFailure = null;
        running = true;
        try
        {
            runtime.FlushImmediate();
            recorder.Begin();
            runtime.Active = recorder;
            try { commands(); }
            catch (Exception error) { recordingFailure = error; }
            finally { runtime.Active = null; }
            // Even failed recording is submitted, so queued work finishes before its buffers are released.
            recorder.ReadBack(result.Native, (ulong)bytes);
            recorder.Submit();
            await Task.Run(recorder.WaitForFence, CancellationToken.None);
            await GameTask.MainThread();
            recorder.Complete();
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(recorder.ReadBackData(bytes)).CopyTo(destination.Span);
        }
        finally { running = false; }
        if (recordingFailure is OperationCanceledException && cancellation.IsCancellationRequested) cancellation.ThrowIfCancellationRequested();
        if (recordingFailure is not null) throw new InvalidOperationException("Tensor batch recording failed.", recordingFailure);
        cancellation.ThrowIfCancellationRequested();
    }

    /// <summary>Releases the batch's command resources. Rejects disposal while a batch is in flight.</summary>
    public void Dispose()
    {
        ThreadSafe.AssertIsMainThread();
        if (disposed) return;
        if (running) throw new InvalidOperationException("Await GPU readback before disposing the tensor batch.");
        disposed = true;
        GpuRuntime.Require().ReleaseBatch(recorder);
    }
}
