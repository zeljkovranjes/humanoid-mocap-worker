using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HumanoidMocapWorker.Codegen;

/// <summary>Reads the Vulkan program out of an s&amp;box .shader_c file. Each compiled combo is a
/// zstd frame (compressed with a dictionary inside vfx_vulkan) that holds a version-4 blob:
/// int32 version, int32 SPIR-V size, the SPIR-V, then reflection the engine reads.</summary>
internal static unsafe partial class ShaderContainer
{
    const uint FrameMagic = 0xFD2FB528, DictionaryMagic = 0xEC30A437;
    // vfx_vulkan embeds a 64 KiB zstd dictionary; the frame header names it by ID.
    const int DictionaryBytes = 64 * 1024, BlobVersion = 4;

    /// <summary>The single compute module in a compiled shader. Throws when there is not
    /// exactly one, or when the engine library lacks the frame's dictionary.</summary>
    internal static byte[] ExtractSpirv(byte[] compiled, byte[] engineLibrary)
    {
        var modules = new List<byte[]>();
        for (var at = 12; at + 4 <= compiled.Length; at++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(compiled.AsSpan(at)) != FrameMagic) continue;
            var size = BinaryPrimitives.ReadInt32LittleEndian(compiled.AsSpan(at - 12));
            var packed = BinaryPrimitives.ReadInt32LittleEndian(compiled.AsSpan(at - 8));
            if (size <= 8 || size != BinaryPrimitives.ReadInt32LittleEndian(compiled.AsSpan(at - 4)) || packed <= 0 || at + packed > compiled.Length) continue;
            var blob = Decompress(compiled.AsSpan(at, packed), size, engineLibrary);
            if (BinaryPrimitives.ReadInt32LittleEndian(blob) != BlobVersion)
                throw new InvalidDataException("Unsupported compiled shader blob version " + BinaryPrimitives.ReadInt32LittleEndian(blob) + ".");
            var spirvBytes = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4));
            if (spirvBytes > 0 && 8 + spirvBytes <= blob.Length) modules.Add(blob.AsSpan(8, spirvBytes).ToArray());
        }
        return modules.Count == 1 ? modules[0]
            : throw new InvalidDataException($"Expected one compiled compute program, found {modules.Count}.");
    }

    static byte[] Decompress(ReadOnlySpan<byte> frame, int size, byte[] engineLibrary)
    {
        fixed (byte* source = frame)
        {
            var id = GetDictionaryId(source, (nuint)frame.Length);
            var dictionary = Dictionary(engineLibrary, id);
            var output = new byte[size];
            var context = CreateContext();
            try
            {
                fixed (byte* target = output)
                fixed (byte* words = dictionary)
                {
                    var written = DecompressWithDictionary(context, target, (nuint)size, source, (nuint)frame.Length, words, (nuint)dictionary.Length);
                    if (IsError(written) != 0 || written != (nuint)size)
                        throw new InvalidDataException($"zstd could not decompress a {frame.Length}-byte shader frame (dictionary {id}).");
                }
            }
            finally { FreeContext(context); }
            return output;
        }
    }

    static byte[] Dictionary(byte[] library, uint id)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, DictionaryMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], id);
        var at = library.AsSpan().IndexOf(header);
        if (at < 0 || at + DictionaryBytes > library.Length)
            throw new InvalidDataException($"The engine library does not contain shader dictionary {id}; regenerate with a matching s&box build.");
        return library.AsSpan(at, DictionaryBytes).ToArray();
    }

    const string Zstd = "libzstd.so.1";
    [LibraryImport(Zstd, EntryPoint = "ZSTD_createDCtx")] private static partial nint CreateContext();
    [LibraryImport(Zstd, EntryPoint = "ZSTD_freeDCtx")] private static partial nuint FreeContext(nint context);
    [LibraryImport(Zstd, EntryPoint = "ZSTD_isError")] private static partial uint IsError(nuint code);
    [LibraryImport(Zstd, EntryPoint = "ZSTD_getDictID_fromFrame")] private static partial uint GetDictionaryId(byte* source, nuint size);
    [LibraryImport(Zstd, EntryPoint = "ZSTD_decompress_usingDict")]
    private static partial nuint DecompressWithDictionary(nint context, byte* destination, nuint capacity, byte* source, nuint size, byte* dictionary, nuint dictionarySize);
}
