using System.Runtime.InteropServices;
using System.Text;

namespace HumanoidMocapWorker.Vulkan;

/// <summary>Where a buffer's memory lives: GPU-only, CPU-written staging, or CPU-read results.</summary>
internal enum MemoryKind { Device, Upload, Readback }

/// <summary>One Vulkan device and its universal queue. Prefers a discrete GPU. Requires
/// VK_KHR_push_descriptor; uses VK_EXT_memory_budget when present. Not thread-safe:
/// create, record and submit on one thread.</summary>
internal sealed unsafe class GpuDevice : IDisposable
{
    const string PushDescriptorExtension = "VK_KHR_push_descriptor", BudgetExtension = "VK_EXT_memory_budget";
    readonly nint instance;
    readonly Vk.MemoryProperties memory;
    readonly int deviceHeap;
    readonly bool hasBudget;
    bool disposed;

    internal nint Physical { get; }
    internal nint Handle { get; }
    internal nint Queue { get; }
    internal uint QueueFamily { get; }
    internal string Name { get; }
    internal delegate* unmanaged<nint, uint, ulong, uint, uint, Vk.WriteDescriptorSet*, void> PushDescriptorSet { get; }

    GpuDevice(nint instance, nint physical, string name)
    {
        this.instance = instance; Physical = physical; Name = name;
        Vk.MemoryProperties properties;
        Vk.GetMemoryProperties(physical, &properties);
        memory = properties;
        deviceHeap = DeviceHeap(properties);
        QueueFamily = UniversalQueue(physical);
        var extensions = Extensions(physical);
        if (!extensions.Contains(PushDescriptorExtension))
            throw new NotSupportedException($"GPU '{name}' lacks {PushDescriptorExtension}, which the worker needs to bind shader buffers.");
        hasBudget = extensions.Contains(BudgetExtension);
        Handle = CreateLogicalDevice(physical, QueueFamily, hasBudget);
        nint queue;
        Vk.GetDeviceQueue(Handle, QueueFamily, 0, &queue);
        Queue = queue;
        var pushName = "vkCmdPushDescriptorSetKHR\0"u8;
        fixed (byte* pointer = pushName)
            PushDescriptorSet = (delegate* unmanaged<nint, uint, ulong, uint, uint, Vk.WriteDescriptorSet*, void>)Vk.GetDeviceProcAddr(Handle, pointer);
        if (PushDescriptorSet == null) throw new NotSupportedException("The Vulkan driver did not return vkCmdPushDescriptorSetKHR.");
    }

    /// <summary>Opens the first discrete GPU, else the first integrated GPU, else any
    /// device. Throws when no Vulkan 1.2 device or required extension is available.</summary>
    internal static GpuDevice Create()
    {
        var instance = CreateInstance();
        try
        {
            uint count = 0;
            Vk.Check(Vk.EnumeratePhysicalDevices(instance, &count, null), "vkEnumeratePhysicalDevices");
            if (count == 0) throw new NotSupportedException("No Vulkan device is available.");
            var devices = stackalloc nint[(int)count];
            Vk.Check(Vk.EnumeratePhysicalDevices(instance, &count, devices), "vkEnumeratePhysicalDevices");
            var chosen = -1; var rank = int.MaxValue;
            for (var index = 0; index < count; index++)
            {
                Vk.PhysicalDeviceProperties properties;
                Vk.GetPhysicalDeviceProperties(devices[index], &properties);
                if (properties.ApiVersion < Vk.ApiVersion12) continue;
                var candidate = properties.DeviceType switch { Vk.DeviceTypeDiscrete => 0, Vk.DeviceTypeIntegrated => 1, _ => 2 };
                if (candidate < rank) { rank = candidate; chosen = index; }
            }
            if (chosen < 0) throw new NotSupportedException("No Vulkan 1.2 device is available.");
            Vk.PhysicalDeviceProperties selected;
            Vk.GetPhysicalDeviceProperties(devices[chosen], &selected);
            return new GpuDevice(instance, devices[chosen], Marshal.PtrToStringUTF8((nint)selected.DeviceName));
        }
        catch { Vk.DestroyInstance(instance, null); throw; }
    }

    /// <summary>Total size of the device-local heap in bytes.</summary>
    internal ulong TotalMemory => memory.HeapSize(deviceHeap);

    /// <summary>The driver's budget and this process's usage of the device-local heap, or
    /// (0, 0) when the driver has no budget extension.</summary>
    internal (ulong Budget, ulong Used) Budget()
    {
        if (!hasBudget) return (0, 0);
        Vk.MemoryBudgetProperties budget = default; budget.SType = Vk.StructureMemoryBudgetProperties;
        Vk.MemoryProperties2 properties = default; properties.SType = Vk.StructureMemoryProperties2; properties.Next = &budget;
        Vk.GetMemoryProperties2(Physical, &properties);
        return (budget.HeapBudget[deviceHeap], budget.HeapUsage[deviceHeap]);
    }

    /// <summary>Creates a buffer with its own allocation. Host-visible kinds stay mapped.
    /// Throws when the device is out of memory or has no suitable memory type.</summary>
    internal DeviceBuffer CreateBuffer(ulong bytes, uint usage, MemoryKind kind)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (bytes == 0) throw new ArgumentOutOfRangeException(nameof(bytes), "GPU buffers need at least one byte.");
        Vk.BufferCreateInfo info = default;
        info.SType = Vk.StructureBufferCreateInfo; info.Size = bytes; info.Usage = usage;
        ulong buffer, allocation = 0;
        Vk.Check(Vk.CreateBuffer(Handle, &info, null, &buffer), "vkCreateBuffer");
        try
        {
            Vk.MemoryRequirements requirements;
            Vk.GetBufferMemoryRequirements(Handle, buffer, &requirements);
            Vk.MemoryAllocateInfo allocate = default;
            allocate.SType = Vk.StructureMemoryAllocateInfo; allocate.AllocationSize = requirements.Size;
            allocate.MemoryTypeIndex = MemoryType(requirements.MemoryTypeBits, kind);
            var result = Vk.AllocateMemory(Handle, &allocate, null, &allocation);
            if (result != Vk.Success)
                throw new OutOfMemoryException($"Could not allocate {bytes} bytes of {kind} GPU memory (VkResult {result}).");
            Vk.Check(Vk.BindBufferMemory(Handle, buffer, allocation, 0), "vkBindBufferMemory");
            void* mapped = null;
            if (kind != MemoryKind.Device) Vk.Check(Vk.MapMemory(Handle, allocation, 0, Vk.WholeSize, 0, &mapped), "vkMapMemory");
            return new DeviceBuffer(this, buffer, allocation, bytes, (byte*)mapped);
        }
        catch
        {
            if (allocation != 0) Vk.FreeMemory(Handle, allocation, null);
            Vk.DestroyBuffer(Handle, buffer, null);
            throw;
        }
    }

    uint MemoryType(uint allowed, MemoryKind kind)
    {
        var (required, preferred) = kind switch
        {
            MemoryKind.Device => (Vk.MemoryDeviceLocal, 0u),
            MemoryKind.Upload => (Vk.MemoryHostVisible | Vk.MemoryHostCoherent, 0u),
            _ => (Vk.MemoryHostVisible | Vk.MemoryHostCoherent, Vk.MemoryHostCached),
        };
        // Staging prefers system memory so it does not compete with model weights for VRAM.
        var avoided = kind == MemoryKind.Device ? Vk.MemoryHostVisible : Vk.MemoryDeviceLocal;
        var best = -1; var bestScore = -1;
        for (var index = 0; index < memory.TypeCount; index++)
        {
            var flags = memory.TypeFlags(index);
            if ((allowed & (1u << index)) == 0 || (flags & required) != required) continue;
            var score = ((flags & preferred) == preferred ? 2 : 0) + ((flags & avoided) == 0 ? 1 : 0);
            if (score > bestScore) { best = index; bestScore = score; }
        }
        if (best < 0) throw new NotSupportedException($"GPU '{Name}' has no memory type for {kind} buffers.");
        return (uint)best;
    }

    static int DeviceHeap(in Vk.MemoryProperties properties)
    {
        var best = -1;
        for (var index = 0; index < properties.HeapCount; index++)
            if ((properties.HeapFlags(index) & Vk.HeapDeviceLocal) != 0 && (best < 0 || properties.HeapSize(index) > properties.HeapSize(best)))
                best = index;
        return best >= 0 ? best : throw new NotSupportedException("The GPU reports no device-local memory heap.");
    }

    static uint UniversalQueue(nint physical)
    {
        uint count = 0;
        Vk.GetQueueFamilyProperties(physical, &count, null);
        var families = stackalloc Vk.QueueFamilyProperties[(int)count];
        Vk.GetQueueFamilyProperties(physical, &count, families);
        // The editor records tensor work on its graphics context, so prefer the universal queue.
        for (uint index = 0; index < count; index++)
            if ((families[index].QueueFlags & (Vk.QueueGraphics | Vk.QueueCompute)) == (Vk.QueueGraphics | Vk.QueueCompute)) return index;
        for (uint index = 0; index < count; index++)
            if ((families[index].QueueFlags & Vk.QueueCompute) != 0) return index;
        throw new NotSupportedException("The GPU has no compute queue.");
    }

    static HashSet<string> Extensions(nint physical)
    {
        uint count = 0;
        Vk.Check(Vk.EnumerateDeviceExtensions(physical, null, &count, null), "vkEnumerateDeviceExtensionProperties");
        var properties = new Vk.ExtensionProperties[count];
        fixed (Vk.ExtensionProperties* pointer = properties)
            Vk.Check(Vk.EnumerateDeviceExtensions(physical, null, &count, pointer), "vkEnumerateDeviceExtensionProperties");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties) names.Add(Marshal.PtrToStringUTF8((nint)property.Name));
        return names;
    }

    static nint CreateInstance()
    {
        var name = "humanoid-mocap-worker\0"u8;
        fixed (byte* application = name)
        {
            Vk.ApplicationInfo app = default;
            app.SType = Vk.StructureApplicationInfo; app.ApplicationName = application; app.ApiVersion = Vk.ApiVersion12;
            Vk.InstanceCreateInfo info = default;
            info.SType = Vk.StructureInstanceCreateInfo; info.ApplicationInfo = &app;
            nint instance;
            Vk.Check(Vk.CreateInstance(&info, null, &instance), "vkCreateInstance");
            return instance;
        }
    }

    static nint CreateLogicalDevice(nint physical, uint family, bool budget)
    {
        var priority = 1f;
        Vk.DeviceQueueCreateInfo queue = default;
        queue.SType = Vk.StructureDeviceQueueCreateInfo; queue.QueueFamilyIndex = family; queue.QueueCount = 1; queue.Priorities = &priority;
        var push = Encoding.ASCII.GetBytes(PushDescriptorExtension + "\0");
        var memoryBudget = Encoding.ASCII.GetBytes(BudgetExtension + "\0");
        fixed (byte* pushName = push)
        fixed (byte* budgetName = memoryBudget)
        {
            var names = stackalloc byte*[] { pushName, budgetName };
            Vk.DeviceCreateInfo info = default;
            info.SType = Vk.StructureDeviceCreateInfo; info.QueueCreateInfoCount = 1; info.QueueCreateInfos = &queue;
            info.ExtensionCount = budget ? 2u : 1u; info.Extensions = names;
            nint device;
            Vk.Check(Vk.CreateDevice(physical, &info, null, &device), "vkCreateDevice");
            return device;
        }
    }

    /// <summary>Waits for all GPU work, then destroys the device and instance. Buffers and
    /// pipelines must be disposed first.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Vk.DeviceWaitIdle(Handle);
        Vk.DestroyDevice(Handle, null);
        Vk.DestroyInstance(instance, null);
    }
}
