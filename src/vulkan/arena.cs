namespace HumanoidMocapWorker.Vulkan;

/// <summary>Bump allocator over mapped host-visible chunks, for per-submission staging and
/// shader constants. Reset only after the submission that used it has completed.</summary>
internal sealed unsafe class HostArena(GpuDevice device, ulong chunkBytes, uint usage) : IDisposable
{
    readonly List<DeviceBuffer> chunks = new();
    int current;
    ulong offset;

    /// <summary>Bytes handed out since the last reset.</summary>
    internal ulong Used { get; private set; }

    /// <summary>Reserves min(bytes, chunk size) contiguous bytes at the given power-of-two
    /// alignment. Returns the chunk, offset and length granted; larger requests take several calls.</summary>
    internal (DeviceBuffer Chunk, ulong Offset, ulong Length) Allocate(ulong bytes, ulong alignment)
    {
        var length = Math.Min(bytes, chunkBytes);
        var start = (offset + alignment - 1) & ~(alignment - 1);
        if (current < chunks.Count && (start > chunkBytes || chunkBytes - start < length)) { current++; start = 0; }
        if (current == chunks.Count) { chunks.Add(device.CreateBuffer(chunkBytes, usage, MemoryKind.Upload)); start = 0; }
        offset = start + length; Used += length;
        return (chunks[current], start, length);
    }

    internal void Reset() { current = 0; offset = 0; Used = 0; }

    public void Dispose()
    {
        foreach (var chunk in chunks) chunk.Dispose();
        chunks.Clear();
    }
}
