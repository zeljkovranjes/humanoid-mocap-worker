using System.Runtime.InteropServices;

namespace HumanoidMocapWorker.Vulkan;

// The subset of the Vulkan 1.2 C API this worker uses, bound directly to the system loader.
// Dispatchable handles are pointers (nint); non-dispatchable handles are 64-bit integers.
internal static unsafe partial class Vk
{
    const string Library = "libvulkan.so.1";

    internal const int Success = 0, Timeout = 2;
    internal const uint ApiVersion12 = (1u << 22) | (2u << 12);
    internal const ulong WholeSize = ulong.MaxValue;

    internal const int StructureApplicationInfo = 0, StructureInstanceCreateInfo = 1, StructureDeviceQueueCreateInfo = 2,
        StructureDeviceCreateInfo = 3, StructureSubmitInfo = 4, StructureMemoryAllocateInfo = 5, StructureFenceCreateInfo = 8,
        StructureBufferCreateInfo = 12, StructureShaderModuleCreateInfo = 16, StructurePipelineShaderStageCreateInfo = 18,
        StructureComputePipelineCreateInfo = 29, StructurePipelineLayoutCreateInfo = 30, StructureDescriptorSetLayoutCreateInfo = 32,
        StructureWriteDescriptorSet = 35, StructureCommandPoolCreateInfo = 39, StructureCommandBufferAllocateInfo = 40,
        StructureCommandBufferBeginInfo = 42, StructureMemoryBarrier = 46, StructureMemoryProperties2 = 1000059006,
        StructureMemoryBudgetProperties = 1000237000;

    internal const uint DeviceTypeIntegrated = 1, DeviceTypeDiscrete = 2;
    internal const uint QueueGraphics = 0x1, QueueCompute = 0x2;
    internal const uint MemoryDeviceLocal = 0x1, MemoryHostVisible = 0x2, MemoryHostCoherent = 0x4, MemoryHostCached = 0x8;
    internal const uint HeapDeviceLocal = 0x1;
    internal const uint BufferTransferSource = 0x1, BufferTransferDestination = 0x2, BufferUniform = 0x10, BufferStorage = 0x20;
    internal const uint DescriptorUniformBuffer = 6, DescriptorStorageBuffer = 7;
    internal const uint StageCompute = 0x20, LayoutPushDescriptor = 0x1, BindPointCompute = 1;
    internal const uint PipelineStageComputeShader = 0x800, PipelineStageTransfer = 0x1000, PipelineStageHost = 0x4000,
        PipelineStageAllCommands = 0x10000;
    internal const uint AccessUniformRead = 0x8, AccessShaderRead = 0x20, AccessShaderWrite = 0x40, AccessTransferRead = 0x800,
        AccessTransferWrite = 0x1000, AccessHostRead = 0x2000, AccessHostWrite = 0x4000, AccessMemoryRead = 0x8000,
        AccessMemoryWrite = 0x10000;
    internal const uint CommandBufferOneTimeSubmit = 0x1, CommandPoolResetBuffer = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ApplicationInfo
    {
        public int SType; public void* Next; public byte* ApplicationName; public uint ApplicationVersion;
        public byte* EngineName; public uint EngineVersion; public uint ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InstanceCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public ApplicationInfo* ApplicationInfo;
        public uint LayerCount; public byte** Layers; public uint ExtensionCount; public byte** Extensions;
    }

    // Only the leading fields are read; the size covers the full 64-bit VkPhysicalDeviceProperties (824 bytes).
    [StructLayout(LayoutKind.Sequential, Size = 1024)]
    internal struct PhysicalDeviceProperties
    {
        public uint ApiVersion, DriverVersion, VendorId, DeviceId, DeviceType;
        public fixed byte DeviceName[256];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct QueueFamilyProperties
    {
        public uint QueueFlags, QueueCount, TimestampValidBits, GranularityWidth, GranularityHeight, GranularityDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryProperties
    {
        public uint TypeCount;
        public fixed uint Types[64]; // 32 × (propertyFlags, heapIndex)
        public uint HeapCount;
        public fixed ulong Heaps[32]; // 16 × (size, flags + padding)

        internal readonly uint TypeFlags(int index) => Types[index * 2];
        internal readonly uint TypeHeap(int index) => Types[index * 2 + 1];
        internal readonly ulong HeapSize(int index) => Heaps[index * 2];
        internal readonly uint HeapFlags(int index) => (uint)Heaps[index * 2 + 1];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryProperties2
    {
        public int SType; public void* Next; public MemoryProperties Properties;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBudgetProperties
    {
        public int SType; public void* Next; public fixed ulong HeapBudget[16]; public fixed ulong HeapUsage[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtensionProperties
    {
        public fixed byte Name[256]; public uint SpecVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceQueueCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public uint QueueFamilyIndex; public uint QueueCount;
        public float* Priorities;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public uint QueueCreateInfoCount;
        public DeviceQueueCreateInfo* QueueCreateInfos; public uint LayerCount; public byte** Layers;
        public uint ExtensionCount; public byte** Extensions; public void* EnabledFeatures;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public ulong Size; public uint Usage; public uint SharingMode;
        public uint QueueFamilyIndexCount; public uint* QueueFamilyIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryRequirements
    {
        public ulong Size, Alignment; public uint MemoryTypeBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryAllocateInfo
    {
        public int SType; public void* Next; public ulong AllocationSize; public uint MemoryTypeIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderModuleCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public nuint CodeSize; public uint* Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DescriptorSetLayoutBinding
    {
        public uint Binding, DescriptorType, DescriptorCount, StageFlags; public ulong* ImmutableSamplers;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DescriptorSetLayoutCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public uint BindingCount; public DescriptorSetLayoutBinding* Bindings;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineLayoutCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public uint SetLayoutCount; public ulong* SetLayouts;
        public uint PushConstantRangeCount; public void* PushConstantRanges;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineShaderStageCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public uint Stage; public ulong Module; public byte* Name;
        public void* SpecializationInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ComputePipelineCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public PipelineShaderStageCreateInfo Stage; public ulong Layout;
        public ulong BasePipeline; public int BasePipelineIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandPoolCreateInfo
    {
        public int SType; public void* Next; public uint Flags; public uint QueueFamilyIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandBufferAllocateInfo
    {
        public int SType; public void* Next; public ulong CommandPool; public uint Level; public uint CommandBufferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandBufferBeginInfo
    {
        public int SType; public void* Next; public uint Flags; public void* InheritanceInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SubmitInfo
    {
        public int SType; public void* Next; public uint WaitSemaphoreCount; public ulong* WaitSemaphores; public uint* WaitStages;
        public uint CommandBufferCount; public nint* CommandBuffers; public uint SignalSemaphoreCount; public ulong* SignalSemaphores;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FenceCreateInfo
    {
        public int SType; public void* Next; public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBarrier
    {
        public int SType; public void* Next; public uint SourceAccess; public uint DestinationAccess;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferCopy
    {
        public ulong SourceOffset, DestinationOffset, Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DescriptorBufferInfo
    {
        public ulong Buffer, Offset, Range;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WriteDescriptorSet
    {
        public int SType; public void* Next; public ulong DestinationSet; public uint Binding; public uint ArrayElement;
        public uint DescriptorCount; public uint DescriptorType; public void* ImageInfo; public DescriptorBufferInfo* BufferInfo;
        public void* TexelBufferView;
    }

    [LibraryImport(Library, EntryPoint = "vkCreateInstance")] internal static partial int CreateInstance(InstanceCreateInfo* info, void* allocator, nint* instance);
    [LibraryImport(Library, EntryPoint = "vkDestroyInstance")] internal static partial void DestroyInstance(nint instance, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkEnumeratePhysicalDevices")] internal static partial int EnumeratePhysicalDevices(nint instance, uint* count, nint* devices);
    [LibraryImport(Library, EntryPoint = "vkGetPhysicalDeviceProperties")] internal static partial void GetPhysicalDeviceProperties(nint device, PhysicalDeviceProperties* properties);
    [LibraryImport(Library, EntryPoint = "vkGetPhysicalDeviceQueueFamilyProperties")] internal static partial void GetQueueFamilyProperties(nint device, uint* count, QueueFamilyProperties* properties);
    [LibraryImport(Library, EntryPoint = "vkGetPhysicalDeviceMemoryProperties")] internal static partial void GetMemoryProperties(nint device, MemoryProperties* properties);
    [LibraryImport(Library, EntryPoint = "vkGetPhysicalDeviceMemoryProperties2")] internal static partial void GetMemoryProperties2(nint device, MemoryProperties2* properties);
    [LibraryImport(Library, EntryPoint = "vkEnumerateDeviceExtensionProperties")] internal static partial int EnumerateDeviceExtensions(nint device, byte* layer, uint* count, ExtensionProperties* properties);
    [LibraryImport(Library, EntryPoint = "vkCreateDevice")] internal static partial int CreateDevice(nint physicalDevice, DeviceCreateInfo* info, void* allocator, nint* device);
    [LibraryImport(Library, EntryPoint = "vkDestroyDevice")] internal static partial void DestroyDevice(nint device, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkGetDeviceQueue")] internal static partial void GetDeviceQueue(nint device, uint family, uint index, nint* queue);
    [LibraryImport(Library, EntryPoint = "vkGetDeviceProcAddr")] internal static partial nint GetDeviceProcAddr(nint device, byte* name);
    [LibraryImport(Library, EntryPoint = "vkDeviceWaitIdle")] internal static partial int DeviceWaitIdle(nint device);
    [LibraryImport(Library, EntryPoint = "vkCreateBuffer")] internal static partial int CreateBuffer(nint device, BufferCreateInfo* info, void* allocator, ulong* buffer);
    [LibraryImport(Library, EntryPoint = "vkDestroyBuffer")] internal static partial void DestroyBuffer(nint device, ulong buffer, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkGetBufferMemoryRequirements")] internal static partial void GetBufferMemoryRequirements(nint device, ulong buffer, MemoryRequirements* requirements);
    [LibraryImport(Library, EntryPoint = "vkAllocateMemory")] internal static partial int AllocateMemory(nint device, MemoryAllocateInfo* info, void* allocator, ulong* memory);
    [LibraryImport(Library, EntryPoint = "vkFreeMemory")] internal static partial void FreeMemory(nint device, ulong memory, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkBindBufferMemory")] internal static partial int BindBufferMemory(nint device, ulong buffer, ulong memory, ulong offset);
    [LibraryImport(Library, EntryPoint = "vkMapMemory")] internal static partial int MapMemory(nint device, ulong memory, ulong offset, ulong size, uint flags, void** data);
    [LibraryImport(Library, EntryPoint = "vkCreateShaderModule")] internal static partial int CreateShaderModule(nint device, ShaderModuleCreateInfo* info, void* allocator, ulong* module);
    [LibraryImport(Library, EntryPoint = "vkDestroyShaderModule")] internal static partial void DestroyShaderModule(nint device, ulong module, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkCreateDescriptorSetLayout")] internal static partial int CreateDescriptorSetLayout(nint device, DescriptorSetLayoutCreateInfo* info, void* allocator, ulong* layout);
    [LibraryImport(Library, EntryPoint = "vkDestroyDescriptorSetLayout")] internal static partial void DestroyDescriptorSetLayout(nint device, ulong layout, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkCreatePipelineLayout")] internal static partial int CreatePipelineLayout(nint device, PipelineLayoutCreateInfo* info, void* allocator, ulong* layout);
    [LibraryImport(Library, EntryPoint = "vkDestroyPipelineLayout")] internal static partial void DestroyPipelineLayout(nint device, ulong layout, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkCreateComputePipelines")] internal static partial int CreateComputePipelines(nint device, ulong cache, uint count, ComputePipelineCreateInfo* infos, void* allocator, ulong* pipelines);
    [LibraryImport(Library, EntryPoint = "vkDestroyPipeline")] internal static partial void DestroyPipeline(nint device, ulong pipeline, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkCreateCommandPool")] internal static partial int CreateCommandPool(nint device, CommandPoolCreateInfo* info, void* allocator, ulong* pool);
    [LibraryImport(Library, EntryPoint = "vkDestroyCommandPool")] internal static partial void DestroyCommandPool(nint device, ulong pool, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkAllocateCommandBuffers")] internal static partial int AllocateCommandBuffers(nint device, CommandBufferAllocateInfo* info, nint* buffers);
    [LibraryImport(Library, EntryPoint = "vkBeginCommandBuffer")] internal static partial int BeginCommandBuffer(nint buffer, CommandBufferBeginInfo* info);
    [LibraryImport(Library, EntryPoint = "vkEndCommandBuffer")] internal static partial int EndCommandBuffer(nint buffer);
    [LibraryImport(Library, EntryPoint = "vkCmdBindPipeline")] internal static partial void CmdBindPipeline(nint buffer, uint bindPoint, ulong pipeline);
    [LibraryImport(Library, EntryPoint = "vkCmdDispatch")] internal static partial void CmdDispatch(nint buffer, uint x, uint y, uint z);
    [LibraryImport(Library, EntryPoint = "vkCmdPipelineBarrier")] internal static partial void CmdPipelineBarrier(nint buffer, uint sourceStages, uint destinationStages, uint dependencyFlags, uint memoryBarrierCount, MemoryBarrier* memoryBarriers, uint bufferBarrierCount, void* bufferBarriers, uint imageBarrierCount, void* imageBarriers);
    [LibraryImport(Library, EntryPoint = "vkCmdCopyBuffer")] internal static partial void CmdCopyBuffer(nint buffer, ulong source, ulong destination, uint regionCount, BufferCopy* regions);
    [LibraryImport(Library, EntryPoint = "vkCmdFillBuffer")] internal static partial void CmdFillBuffer(nint buffer, ulong destination, ulong offset, ulong size, uint data);
    [LibraryImport(Library, EntryPoint = "vkCreateFence")] internal static partial int CreateFence(nint device, FenceCreateInfo* info, void* allocator, ulong* fence);
    [LibraryImport(Library, EntryPoint = "vkDestroyFence")] internal static partial void DestroyFence(nint device, ulong fence, void* allocator);
    [LibraryImport(Library, EntryPoint = "vkResetFences")] internal static partial int ResetFences(nint device, uint count, ulong* fences);
    [LibraryImport(Library, EntryPoint = "vkWaitForFences")] internal static partial int WaitForFences(nint device, uint count, ulong* fences, uint waitAll, ulong timeout);
    [LibraryImport(Library, EntryPoint = "vkQueueSubmit")] internal static partial int QueueSubmit(nint queue, uint count, SubmitInfo* submits, ulong fence);

    /// <summary>Throws with the failed call's name and VkResult when a command did not succeed.</summary>
    internal static void Check(int result, string call)
    {
        if (result != Success) throw new InvalidOperationException($"Vulkan {call} failed with VkResult {result}.");
    }
}
