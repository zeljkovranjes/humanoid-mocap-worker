using System.Runtime.InteropServices;

namespace HumanoidMocapWorker.Codegen;

/// <summary>What a compute module declares in descriptor set 0: its thread-group size, the
/// scalar members of its uniform block and its structured buffers.</summary>
internal sealed record SpirvLayout(int[] LocalSize, SpirvUniform Uniform, SpirvStorage[] Storage);

internal sealed record SpirvUniform(uint Binding, (int Offset, string Type)[] Members)
{
    internal int Size => Members.Length == 0 ? 0 : Members.Max(member => member.Offset) + 4;
}

internal sealed record SpirvStorage(uint Binding, string ElementType, bool NonWritable);

/// <summary>Minimal SPIR-V reflection for the tensor shaders: rejects anything it does not
/// fully understand (other descriptor kinds, sets, non-scalar members).</summary>
internal static class Spirv
{
    const uint Magic = 0x07230203;
    const int OpExecutionMode = 16, OpTypeInt = 21, OpTypeFloat = 22, OpTypeRuntimeArray = 29, OpTypeStruct = 30,
        OpTypePointer = 32, OpVariable = 59, OpDecorate = 71, OpMemberDecorate = 72;
    const uint ModeLocalSize = 17, DecorationNonWritable = 24, DecorationBinding = 33, DecorationSet = 34, DecorationOffset = 35;
    const uint StorageUniformConstant = 0, StorageUniform = 2, StorageBuffer = 12;

    /// <summary>Parses a module. Throws InvalidDataException for malformed or unsupported modules.</summary>
    internal static SpirvLayout Reflect(byte[] module)
    {
        if (module.Length < 20 || module.Length % 4 != 0) throw new InvalidDataException("SPIR-V must be whole words with a header.");
        var words = MemoryMarshal.Cast<byte, uint>(module);
        if (words[0] != Magic) throw new InvalidDataException("Missing SPIR-V magic number.");
        int[] localSize = null;
        var scalars = new Dictionary<uint, string>(); var arrays = new Dictionary<uint, uint>();
        var structs = new Dictionary<uint, uint[]>(); var pointers = new Dictionary<uint, (uint Storage, uint Type)>();
        var bindings = new Dictionary<uint, uint>(); var sets = new Dictionary<uint, uint>(); var readOnly = new HashSet<uint>();
        var offsets = new Dictionary<(uint, uint), int>(); var variables = new List<(uint Id, uint Type, uint Storage)>();
        for (var index = 5; index < words.Length;)
        {
            var count = (int)(words[index] >> 16); var opcode = (int)(words[index] & 0xffff);
            if (count == 0 || index + count > words.Length) throw new InvalidDataException($"Malformed SPIR-V instruction at word {index}.");
            var operands = words.Slice(index + 1, count - 1);
            switch (opcode)
            {
                case OpExecutionMode when operands[1] == ModeLocalSize: localSize = [(int)operands[2], (int)operands[3], (int)operands[4]]; break;
                case OpTypeInt when operands[1] == 32: scalars[operands[0]] = operands[2] == 0 ? "uint" : "int"; break;
                case OpTypeFloat when operands[1] == 32: scalars[operands[0]] = "float"; break;
                case OpTypeRuntimeArray: arrays[operands[0]] = operands[1]; break;
                case OpTypeStruct: structs[operands[0]] = operands[1..].ToArray(); break;
                case OpTypePointer: pointers[operands[0]] = (operands[1], operands[2]); break;
                case OpVariable: variables.Add((operands[1], operands[0], operands[2])); break;
                case OpDecorate when operands[1] == DecorationBinding: bindings[operands[0]] = operands[2]; break;
                case OpDecorate when operands[1] == DecorationSet: sets[operands[0]] = operands[2]; break;
                case OpDecorate when operands[1] == DecorationNonWritable: readOnly.Add(operands[0]); break;
                case OpMemberDecorate when operands[2] == DecorationOffset: offsets[(operands[0], operands[1])] = (int)operands[3]; break;
            }
            index += count;
        }
        if (localSize is null) throw new InvalidDataException("The module has no LocalSize execution mode.");
        SpirvUniform uniform = null; var storage = new List<SpirvStorage>();
        foreach (var (id, type, storageClass) in variables)
        {
            if (storageClass == StorageUniformConstant) throw new InvalidDataException("Textures and samplers are not supported.");
            if (storageClass is not (StorageUniform or StorageBuffer)) continue;
            if (!sets.TryGetValue(id, out var set) || set != 0 || !bindings.TryGetValue(id, out var binding))
                throw new InvalidDataException($"Resource %{id} is not bound in descriptor set 0.");
            var block = structs[pointers[type].Type];
            if (storageClass == StorageUniform)
            {
                if (uniform is not null) throw new InvalidDataException("More than one uniform block.");
                var members = new (int, string)[block.Length];
                for (var member = 0; member < block.Length; member++)
                    members[member] = (offsets[(pointers[type].Type, (uint)member)], scalars.GetValueOrDefault(block[member])
                        ?? throw new InvalidDataException("Uniform members must be 32-bit scalars."));
                uniform = new SpirvUniform(binding, members);
            }
            else
            {
                if (block.Length != 1 || !arrays.TryGetValue(block[0], out var element) || !scalars.TryGetValue(element, out var elementType))
                    throw new InvalidDataException("Storage buffers must hold one runtime array of 32-bit scalars.");
                storage.Add(new SpirvStorage(binding, elementType, readOnly.Contains(id)));
            }
        }
        return new SpirvLayout(localSize, uniform, storage.OrderBy(slot => slot.Binding).ToArray());
    }
}
