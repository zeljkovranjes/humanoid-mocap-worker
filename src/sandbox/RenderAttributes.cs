using System.Buffers.Binary;
using HumanoidMocapWorker;

namespace Sandbox;

/// <summary>Named shader inputs. Scalars never set read as zero, as in the engine;
/// a buffer the shader declares must be set before dispatch.</summary>
public sealed class RenderAttributes
{
    readonly Dictionary<string, GpuBuffer> buffers = new(StringComparer.Ordinal);
    readonly Dictionary<string, (uint Bits, bool IsFloat)> scalars = new(StringComparer.Ordinal);

    public void Set(string name, GpuBuffer value) => buffers[name] = value;
    public void Set(string name, int value) => scalars[name] = ((uint)value, false);
    public void Set(string name, float value) => scalars[name] = (BitConverter.SingleToUInt32Bits(value), true);

    internal GpuBuffer Buffer(string name, string shader)
    {
        if (!buffers.TryGetValue(name, out var buffer) || buffer is null || !buffer.IsValid)
            throw new InvalidOperationException($"Shader '{shader}' needs a valid buffer for attribute '{name}'.");
        return buffer;
    }

    /// <summary>Packs scalar attributes into the shader's uniform block. An int sets a float
    /// member by conversion; a float cannot set an integer member.</summary>
    internal void WriteConstants(ConstantBlock block, Span<byte> destination, string shader)
    {
        destination.Clear();
        foreach (var member in block.Members)
        {
            if (!scalars.TryGetValue(member.Name, out var value)) continue;
            var bits = member.Type switch
            {
                "float" when value.IsFloat => value.Bits,
                "float" => BitConverter.SingleToUInt32Bits((int)value.Bits),
                "uint" or "int" when !value.IsFloat => value.Bits,
                _ => throw new InvalidOperationException($"Shader '{shader}' attribute '{member.Name}' is {member.Type}, not a float."),
            };
            BinaryPrimitives.WriteUInt32LittleEndian(destination[member.Offset..], bits);
        }
    }
}
