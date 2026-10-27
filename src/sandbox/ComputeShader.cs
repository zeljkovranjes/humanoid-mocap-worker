using HumanoidMocapWorker;
using HumanoidMocapWorker.Vulkan;

namespace Sandbox;

/// <summary>s&amp;box's compute shader API, backed by SPIR-V extracted from the editor's
/// compiled shaders. Attributes persist between dispatches, as in the engine.</summary>
public sealed class ComputeShader
{
    readonly ShaderProgram program;
    readonly (uint Binding, DeviceBuffer Buffer)[] bindings;

    /// <summary>Attributes passed to the shader on dispatch, bound by name.</summary>
    public RenderAttributes Attributes { get; } = new();

    /// <summary>Loads the shader at a resource path such as "shaders/x/y_compute.shader".
    /// Throws when it was not extracted or the GPU runtime is not started.</summary>
    public ComputeShader(string path)
    {
        program = GpuRuntime.Require().Program(path);
        bindings = new (uint, DeviceBuffer)[program.Entry.Buffers.Length];
    }

    /// <summary>Records a dispatch covering the given thread counts, rounded up to whole
    /// thread groups. Throws for counts below one or missing buffer attributes.</summary>
    public void Dispatch(int threadsX = 32, int threadsY = 32, int threadsZ = 32)
    {
        var runtime = GpuRuntime.Require();
        var entry = program.Entry;
        uint x = program.Groups(threadsX, 0), y = program.Groups(threadsY, 1), z = program.Groups(threadsZ, 2);
        for (var index = 0; index < bindings.Length; index++)
        {
            var slot = entry.Buffers[index];
            bindings[index] = (slot.Binding, Attributes.Buffer(slot.Name, entry.Path).Native);
        }
        Span<byte> constants = stackalloc byte[entry.Constants?.Size ?? 0];
        if (entry.Constants is not null) Attributes.WriteConstants(entry.Constants, constants, entry.Path);
        runtime.Recorder.Dispatch(program.Pipeline, x, y, z, entry.Constants?.Binding ?? 0, constants, bindings);
        Array.Clear(bindings);
    }
}
