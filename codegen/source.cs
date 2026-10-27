using System.Text.RegularExpressions;

namespace HumanoidMocapWorker.Codegen;

/// <summary>An attribute declared in a .shader CS block, in source order.</summary>
internal sealed record Declaration(string Name, string Type, bool IsBuffer, bool IsWritable);

/// <summary>Reads attribute declarations and pairs them with the compiled module. The
/// editor's compiler gives read-only buffers, writable buffers and uniform members their
/// slots in declaration order; this mapping is checked against every SPIR-V type.</summary>
internal static partial class ShaderSource
{
    [GeneratedRegex(@"^\s*(?:(?<rw>RW)?StructuredBuffer\s*<\s*(?<type>\w+)\s*>|(?<type>uint|int|float))\s+\w+\s*<\s*Attribute\(\s*""(?<name>\w+)""\s*\)\s*;[^>]*>\s*;", RegexOptions.Multiline)]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"^CS\s*\{", RegexOptions.Multiline)]
    private static partial Regex ComputeBlock();

    /// <summary>Declarations inside the CS block. Throws when the block is missing.</summary>
    internal static Declaration[] Declarations(string source)
    {
        var start = ComputeBlock().Match(source);
        if (!start.Success) throw new InvalidDataException("The shader has no CS block.");
        var depth = 0; var end = start.Index + start.Length - 1;
        for (; end < source.Length; end++)
        {
            if (source[end] == '{') depth++;
            else if (source[end] == '}' && --depth == 0) break;
        }
        var block = source[start.Index..end];
        return AttributePattern().Matches(block).Select(match => new Declaration(match.Groups["name"].Value, match.Groups["type"].Value,
            match.Groups["rw"].Success || match.Value.Contains("StructuredBuffer"), match.Groups["rw"].Success)).ToArray();
    }

    /// <summary>Names each SPIR-V slot. Throws when counts, types or binding order disagree.</summary>
    internal static (ConstantBlock Constants, BufferSlot[] Buffers) Bind(Declaration[] declarations, SpirvLayout layout, string shader)
    {
        var readOnly = declarations.Where(item => item.IsBuffer && !item.IsWritable).ToArray();
        var writable = declarations.Where(item => item.IsWritable).ToArray();
        var scalars = declarations.Where(item => !item.IsBuffer).ToArray();
        if (layout.Storage.Length != readOnly.Length + writable.Length)
            throw new InvalidDataException($"{shader}: {layout.Storage.Length} storage buffers compiled for {readOnly.Length + writable.Length} declared.");
        var buffers = new BufferSlot[layout.Storage.Length];
        for (var index = 0; index < buffers.Length; index++)
        {
            var isWritable = index >= readOnly.Length;
            var declared = isWritable ? writable[index - readOnly.Length] : readOnly[index];
            var compiled = layout.Storage[index];
            if (compiled.ElementType != declared.Type || isWritable && compiled.NonWritable)
                throw new InvalidDataException($"{shader}: binding {compiled.Binding} ({compiled.ElementType}) does not match '{declared.Name}' ({declared.Type}).");
            buffers[index] = new BufferSlot(declared.Name, compiled.Binding, isWritable);
        }
        var members = layout.Uniform?.Members ?? [];
        if (members.Length != scalars.Length)
            throw new InvalidDataException($"{shader}: {members.Length} uniform members compiled for {scalars.Length} declared scalars.");
        var slots = new ConstantSlot[members.Length];
        for (var index = 0; index < slots.Length; index++)
        {
            if (members[index].Type != scalars[index].Type)
                throw new InvalidDataException($"{shader}: uniform member {index} is {members[index].Type}, '{scalars[index].Name}' is {scalars[index].Type}.");
            slots[index] = new ConstantSlot(scalars[index].Name, members[index].Offset, members[index].Type);
        }
        var constants = layout.Uniform is null ? null : new ConstantBlock(layout.Uniform.Binding, layout.Uniform.Size, slots);
        return (constants, buffers);
    }
}
