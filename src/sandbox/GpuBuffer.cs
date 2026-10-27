using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HumanoidMocapWorker;
using HumanoidMocapWorker.Vulkan;

namespace Sandbox;

/// <summary>s&amp;box's structured GPU buffer. Contents start zeroed. Uploads, copies and
/// fills are recorded in order with dispatches; dispose on the main thread.</summary>
public class GpuBuffer : IDisposable
{
    const uint Usage = Vk.BufferStorage | Vk.BufferTransferSource | Vk.BufferTransferDestination;
    internal DeviceBuffer Native { get; private set; }

    /// <summary>Number of elements in the buffer.</summary>
    public int ElementCount { get; }
    /// <summary>Size of one element in bytes.</summary>
    public int ElementSize { get; }
    public bool IsValid => Native is not null;

    private protected GpuBuffer(int elementCount, int elementSize)
    {
        if (elementCount <= 0) throw new ArgumentException("Element count must be greater than zero.");
        if (elementSize <= 0 || elementSize % 4 != 0) throw new ArgumentException("Structured buffer elements must be a positive multiple of 4 bytes.");
        if ((ulong)elementSize * (ulong)elementCount > uint.MaxValue) throw new InvalidOperationException("Buffer can't exceed 4GB");
        var runtime = GpuRuntime.Require();
        Native = runtime.Device.CreateBuffer((ulong)elementSize * (ulong)elementCount, Usage, MemoryKind.Device);
        ElementCount = elementCount; ElementSize = elementSize;
        // Vulkan does not clear new allocations; zero them so no result depends on stale memory.
        runtime.Recorder.Fill(Native, 0);
    }

    /// <summary>Uploads data at an element offset, ordered before later GPU work.</summary>
    public void SetData<T>(ReadOnlySpan<T> data, int elementOffset = 0) where T : unmanaged
    {
        var native = Require();
        var bytes = MemoryMarshal.AsBytes(data);
        var offset = (long)elementOffset * Unsafe.SizeOf<T>();
        if (elementOffset < 0 || offset + bytes.Length > (long)native.Size)
            throw new ArgumentOutOfRangeException(nameof(data), $"SetData length: {bytes.Length} at {offset} exceeds size of GpuBuffer: {native.Size}");
        var runtime = GpuRuntime.Require();
        runtime.Recorder.Upload(native, (ulong)offset, bytes);
        runtime.LimitStaging();
    }

    /// <summary>Fills the whole buffer with a repeated 32-bit value.</summary>
    public void Clear(uint value = 0) => GpuRuntime.Require().Recorder.Fill(Require(), value);

    private protected DeviceBuffer Require() => Native ?? throw new ObjectDisposedException(nameof(GpuBuffer));

    /// <summary>Releases the buffer once GPU work using it has finished.</summary>
    public void Dispose()
    {
        ThreadSafe.AssertIsMainThread();
        if (Native is null) return;
        GpuRuntime.Require().Release(Native);
        Native = null;
    }
}

/// <summary>A typed structured buffer.</summary>
public class GpuBuffer<T> : GpuBuffer where T : unmanaged
{
    public GpuBuffer(int elementCount) : base(elementCount, Unsafe.SizeOf<T>()) { }

    /// <summary>Copies all elements to <paramref name="destination"/>.</summary>
    public void CopyTo(GpuBuffer<T> destination) => CopyTo(destination, 0, 0, ElementCount);

    /// <summary>Copies the first elements to <paramref name="destination"/>.</summary>
    public void CopyTo(GpuBuffer<T> destination, int elementCount) => CopyTo(destination, 0, 0, elementCount);

    /// <summary>Copies a range of elements to <paramref name="destination"/>. Throws for ranges outside either buffer.</summary>
    public void CopyTo(GpuBuffer<T> destination, int sourceOffset, int destinationOffset, int elementCount)
    {
        var source = Require();
        ArgumentNullException.ThrowIfNull(destination);
        var target = destination.Native ?? throw new ObjectDisposedException(nameof(destination));
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(checked(sourceOffset + elementCount), ElementCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(checked(destinationOffset + elementCount), destination.ElementCount);
        if (elementCount == 0) return;
        var size = (ulong)Unsafe.SizeOf<T>();
        GpuRuntime.Require().Recorder.Copy(source, (ulong)sourceOffset * size, target, (ulong)destinationOffset * size, (ulong)elementCount * size);
    }
}
