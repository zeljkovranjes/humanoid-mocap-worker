using HumanoidMocapWorker;
using HumanoidMocapWorker.Codegen;

static class ShaderTests
{
    internal static Task Run()
    {
        var table = ShaderTable.Load();
        Check.That(table.Shaders.Length == 10 && table.Compiler.StartsWith("libvfx_vulkan.so ", StringComparison.Ordinal),
            "The embedded table must describe all ten tensor shaders and their compiler");
        foreach (var entry in table.Shaders)
        {
            var layout = Spirv.Reflect(ShaderTable.Spirv(entry));
            Check.That(layout.LocalSize.SequenceEqual(entry.LocalSize), entry.Path + ": thread-group size differs from its SPIR-V");
            Check.That(layout.Storage.Select(slot => slot.Binding).SequenceEqual(entry.Buffers.Select(slot => slot.Binding)),
                entry.Path + ": buffer bindings differ from its SPIR-V");
            Check.That(layout.Uniform is not null && layout.Uniform.Binding == entry.Constants.Binding && layout.Uniform.Size == entry.Constants.Size
                && layout.Uniform.Members.SequenceEqual(entry.Constants.Members.Select(member => (member.Offset, member.Type))),
                entry.Path + ": uniform members differ from its SPIR-V");
        }
        var linear = new SpirvLayout([16, 16, 1], new SpirvUniform(0, [(0, "uint"), (4, "float")]),
            [new SpirvStorage(46, "float", true), new SpirvStorage(174, "float", false)]);
        var declared = new[] { new Declaration("Output", "float", true, true), new Declaration("Input", "float", true, false),
            new Declaration("Rows", "uint", false, false), new Declaration("Epsilon", "float", false, false) };
        var (constants, buffers) = ShaderSource.Bind(declared, linear, "test");
        Check.That(buffers[0] == new BufferSlot("Input", 46, false) && buffers[1] == new BufferSlot("Output", 174, true),
            "Read-only buffers take the low bindings and writable buffers the high ones, each in declaration order");
        Check.That(constants.Members.Select(member => member.Name).SequenceEqual(["Rows", "Epsilon"]), "Uniform members follow declaration order");
        Check.Throws<InvalidDataException>(() => ShaderSource.Bind([declared[0], declared[1], declared[3], declared[2]], linear, "test"),
            "A uniform type mismatch must be rejected");
        Check.Throws<InvalidDataException>(() => ShaderSource.Bind(declared[1..], linear, "test"), "A missing declaration must be rejected");
        var parsed = ShaderSource.Declarations("""
            COMMON { uint Ignored < Attribute("Ignored"); >; }
            CS
            {
                StructuredBuffer<uint> Ranges < Attribute("Ranges"); >;
                RWStructuredBuffer<float> Output < Attribute("Output"); >;
                float Amount < Attribute("Amount"); Default(0); >;
                void MainCs() { }
            }
            """);
        Check.That(parsed.SequenceEqual([new Declaration("Ranges", "uint", true, false), new Declaration("Output", "float", true, true),
            new Declaration("Amount", "float", false, false)]), "Only CS-block attributes are read, in order");
        return Task.CompletedTask;
    }
}
