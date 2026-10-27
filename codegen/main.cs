using System.Security.Cryptography;
using System.Text.Json;
using HumanoidMocapWorker;
using HumanoidMocapWorker.Codegen;

// Extracts the SPIR-V the s&box editor runs for each tensor-inference compute shader, so the
// worker dispatches byte-identical programs without the editor installed.
if (args.Length != 3)
{
    Console.Error.WriteLine("usage: shader-codegen <s&box game directory> <tensor-inference Assets directory> <output directory>");
    return 2;
}
var (game, assets, output) = (Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
var libraryPath = Path.Combine(game, "bin", "linuxsteamrt64", "libvfx_vulkan.so");
if (!File.Exists(libraryPath)) throw new FileNotFoundException("The s&box shader compiler library was not found.", libraryPath);
var library = await File.ReadAllBytesAsync(libraryPath);
var compiler = "libvfx_vulkan.so " + Convert.ToHexStringLower(SHA256.HashData(library));
Directory.CreateDirectory(output);
var entries = new List<ShaderEntry>();
var sums = new List<string>();
foreach (var shaderPath in Directory.EnumerateFiles(assets, "*.shader", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    var relative = Path.GetRelativePath(assets, shaderPath).Replace('\\', '/');
    var compiledPath = shaderPath + "_c";
    if (!File.Exists(compiledPath) || File.GetLastWriteTimeUtc(compiledPath) < File.GetLastWriteTimeUtc(shaderPath))
        throw new InvalidDataException($"{relative} has no up-to-date .shader_c; open the project in the s&box editor to compile it.");
    var source = await File.ReadAllBytesAsync(shaderPath);
    var spirv = ShaderContainer.ExtractSpirv(await File.ReadAllBytesAsync(compiledPath), library);
    var layout = Spirv.Reflect(spirv);
    var (constants, buffers) = ShaderSource.Bind(ShaderSource.Declarations(System.Text.Encoding.UTF8.GetString(source)), layout, relative);
    var spirvName = Path.GetFileNameWithoutExtension(shaderPath) + ".spv";
    await File.WriteAllBytesAsync(Path.Combine(output, spirvName), spirv);
    var sourceHash = Convert.ToHexString(SHA256.HashData(source));
    entries.Add(new ShaderEntry(relative, spirvName, sourceHash, layout.LocalSize, constants, buffers));
    sums.Add(sourceHash + "  " + Path.GetFileName(shaderPath));
    Console.WriteLine($"{relative}: {spirv.Length} bytes SPIR-V, {buffers.Length} buffers, {constants?.Members.Length ?? 0} constants");
}
if (entries.Count == 0) throw new InvalidDataException("No .shader files were found under " + assets);
var json = JsonSerializer.Serialize(new ShaderTable(compiler, entries.ToArray()), new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(output, ShaderTable.ResourceName), json + "\n");
await File.WriteAllLinesAsync(Path.Combine(output, "sources.sha256"), sums);
return 0;
