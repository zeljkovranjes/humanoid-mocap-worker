using System.Reflection;
using System.Text.Json;

namespace HumanoidMocapWorker;

/// <summary>The compute shaders extracted from the editor's compiled .shader_c files, with
/// the attribute layout the editor binds by name. Written by codegen/, embedded at build.</summary>
internal sealed record ShaderTable(string Compiler, ShaderEntry[] Shaders)
{
    internal const string ResourceName = "shaders.json";

    /// <summary>Loads the embedded table. Throws when the build has no generated shaders.</summary>
    internal static ShaderTable Load()
    {
        using var stream = Resource(ResourceName);
        return JsonSerializer.Deserialize<ShaderTable>(stream)
            ?? throw new InvalidDataException("The embedded shader table is empty.");
    }

    /// <summary>The SPIR-V bytes for an entry.</summary>
    internal static byte[] Spirv(ShaderEntry entry)
    {
        using var stream = Resource(entry.Spirv);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidDataException($"Embedded shader resource '{name}' is missing; run codegen to regenerate src/gen.");
}

/// <summary>One compute shader: its resource path as the C# code names it, the SPIR-V file,
/// the SHA-256 of the .shader source it was compiled from, and its thread-group size.</summary>
internal sealed record ShaderEntry(string Path, string Spirv, string SourceSha256, int[] LocalSize,
    ConstantBlock Constants, BufferSlot[] Buffers);

/// <summary>The $Globals uniform block: binding, byte size and named members.</summary>
internal sealed record ConstantBlock(uint Binding, int Size, ConstantSlot[] Members);

/// <summary>A scalar attribute at a byte offset; Type is "uint", "int" or "float".</summary>
internal sealed record ConstantSlot(string Name, int Offset, string Type);

/// <summary>A structured-buffer attribute bound whole at a storage-buffer binding.</summary>
internal sealed record BufferSlot(string Name, uint Binding, bool Writable);
