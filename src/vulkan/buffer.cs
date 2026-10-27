namespace HumanoidMocapWorker.Vulkan;

/// <summary>A VkBuffer with its own memory allocation. Mapped is non-null for host-visible
/// memory. Dispose only after GPU work using it has completed.</summary>
internal sealed unsafe class DeviceBuffer : IDisposable
{
    readonly GpuDevice device;
    readonly ulong memory;

    internal ulong Handle { get; private set; }
    internal ulong Size { get; }
    internal byte* Mapped { get; }

    internal DeviceBuffer(GpuDevice device, ulong handle, ulong memory, ulong size, byte* mapped)
    {
        this.device = device; Handle = handle; this.memory = memory; Size = size; Mapped = mapped;
    }

    public void Dispose()
    {
        if (Handle == 0) return;
        Vk.DestroyBuffer(device.Handle, Handle, null);
        Vk.FreeMemory(device.Handle, memory, null);
        Handle = 0;
    }
}
