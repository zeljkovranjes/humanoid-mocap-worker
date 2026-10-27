namespace HumanoidMocapWorker.Vulkan;

/// <summary>A compute pipeline whose single descriptor set is pushed per dispatch.
/// Bindings list each (binding, descriptor type) the SPIR-V declares in set 0.</summary>
internal sealed unsafe class ComputePipeline : IDisposable
{
    readonly GpuDevice device;
    readonly ulong module, setLayout;

    internal ulong Handle { get; private set; }
    internal ulong Layout { get; }

    /// <summary>Compiles the module for entry point MainCs. Throws when the driver rejects it.</summary>
    internal ComputePipeline(GpuDevice device, ReadOnlySpan<byte> spirv, ReadOnlySpan<(uint Binding, uint Type)> bindings)
    {
        if (spirv.Length == 0 || spirv.Length % 4 != 0) throw new ArgumentException("SPIR-V must be a nonempty sequence of 32-bit words.", nameof(spirv));
        this.device = device;
        try
        {
            fixed (byte* code = spirv)
            {
                Vk.ShaderModuleCreateInfo info = default;
                info.SType = Vk.StructureShaderModuleCreateInfo; info.CodeSize = (nuint)spirv.Length; info.Code = (uint*)code;
                ulong created;
                Vk.Check(Vk.CreateShaderModule(device.Handle, &info, null, &created), "vkCreateShaderModule");
                module = created;
            }
            var layoutBindings = stackalloc Vk.DescriptorSetLayoutBinding[bindings.Length];
            for (var index = 0; index < bindings.Length; index++)
                layoutBindings[index] = new Vk.DescriptorSetLayoutBinding
                {
                    Binding = bindings[index].Binding, DescriptorType = bindings[index].Type, DescriptorCount = 1, StageFlags = Vk.StageCompute,
                };
            Vk.DescriptorSetLayoutCreateInfo setInfo = default;
            setInfo.SType = Vk.StructureDescriptorSetLayoutCreateInfo; setInfo.Flags = Vk.LayoutPushDescriptor;
            setInfo.BindingCount = (uint)bindings.Length; setInfo.Bindings = layoutBindings;
            ulong set;
            Vk.Check(Vk.CreateDescriptorSetLayout(device.Handle, &setInfo, null, &set), "vkCreateDescriptorSetLayout");
            setLayout = set;
            Vk.PipelineLayoutCreateInfo layoutInfo = default;
            layoutInfo.SType = Vk.StructurePipelineLayoutCreateInfo; layoutInfo.SetLayoutCount = 1; layoutInfo.SetLayouts = &set;
            ulong layout;
            Vk.Check(Vk.CreatePipelineLayout(device.Handle, &layoutInfo, null, &layout), "vkCreatePipelineLayout");
            Layout = layout;
            var entry = "MainCs\0"u8;
            fixed (byte* name = entry)
            {
                Vk.ComputePipelineCreateInfo info = default;
                info.SType = Vk.StructureComputePipelineCreateInfo; info.Layout = layout; info.BasePipelineIndex = -1;
                info.Stage.SType = Vk.StructurePipelineShaderStageCreateInfo; info.Stage.Stage = Vk.StageCompute;
                info.Stage.Module = module; info.Stage.Name = name;
                ulong pipeline;
                Vk.Check(Vk.CreateComputePipelines(device.Handle, 0, 1, &info, null, &pipeline), "vkCreateComputePipelines");
                Handle = pipeline;
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (Handle != 0) Vk.DestroyPipeline(device.Handle, Handle, null);
        if (Layout != 0) Vk.DestroyPipelineLayout(device.Handle, Layout, null);
        if (setLayout != 0) Vk.DestroyDescriptorSetLayout(device.Handle, setLayout, null);
        if (module != 0) Vk.DestroyShaderModule(device.Handle, module, null);
        Handle = 0;
    }
}
