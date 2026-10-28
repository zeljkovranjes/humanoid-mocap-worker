using HumanoidMocapWorker;
using Sandbox;
using HumanoidMocapX.TensorInference;

static class GpuTests
{
    const int Count = 1000;

    internal static Task Run()
    {
        MainLoop.Run(RunOnMainThread);
        return Task.CompletedTask;
    }

    static async Task<bool> RunOnMainThread()
    {
        GpuRuntime gpu;
        try { gpu = GpuRuntime.Start(); }
        catch (Exception error) when (error is NotSupportedException or DllNotFoundException or InvalidOperationException)
        { throw new SkipException("no usable Vulkan device: " + error.Message); }
        using (gpu)
        {
            var kernels = new TensorKernels();
            using var first = new GpuBuffer<float>(Count);
            using var second = new GpuBuffer<float>(Count);
            using var sum = new GpuBuffer<float>(Count);
            using var batch = new TensorBatch();
            var x = new float[Count]; var y = new float[Count]; var result = new float[Count];
            for (var index = 0; index < Count; index++) { x[index] = index * 0.37f - 11; y[index] = 1f / (index + 3); }
            await batch.RunAsync(() => { }, sum, result);
            Check.That(result.All(value => value == 0), "New buffers must start zeroed");
            first.SetData<float>(x); second.SetData<float>(y);
            await batch.RunAsync(() => kernels.Add(first, second, sum, Count), sum, result);
            for (var index = 0; index < Count; index++)
                Check.That(result[index] == x[index] + y[index], $"GPU add differs at {index}: {result[index]} vs {x[index] + y[index]}");
            await batch.RunAsync(() => { sum.Clear(); first.CopyTo(sum, 2, 1, 3); }, sum, result.AsMemory(0, 5));
            Check.That(result.AsSpan(0, 5).SequenceEqual([0, x[2], x[3], x[4], 0]), "Clear and ranged copy must run in recorded order");
            var failure = await Record(batch, () => throw new TimeZoneNotFoundException(), sum, result);
            Check.That(failure is InvalidOperationException { InnerException: TimeZoneNotFoundException }, "Recording failures must propagate");
            Check.Throws<ArgumentException>(() => new ComputeShader("shaders/missing_compute.shader"), "Unknown shaders must be rejected");
        }
        return true;
    }

    static async Task<Exception> Record(TensorBatch batch, Action commands, GpuBuffer<float> buffer, float[] result)
    {
        try { await batch.RunAsync(commands, buffer, result); return null; }
        catch (Exception error) { return error; }
    }
}
