using System.Text.Json;
using HumanoidMocapX;
using HumanoidMocapX.EditorTools;
using HumanoidMocapX.Inference;

namespace HumanoidMocapWorker;

/// <summary>A predecoded validation clip: raw RGBA frames on the 30 fps timeline plus the
/// model files and rigs to use. The same JSON the editor's ClipChecks reads.</summary>
internal sealed record Fixture(string RgbaPath, int Width, int Height, int Frames, string SourceVideo,
    string FlowPath, string SamPath, string MhrPath, string OmegaPath, string DetectorPath, string SmplPath, string RigDirectory,
    int CameraInterval = 10, FlowModel Model = FlowModel.Latest)
{
    /// <summary>Reads and validates a fixture file. Throws for missing fields or frame counts outside 1–5000.</summary>
    internal static async Task<Fixture> LoadAsync(string path, CancellationToken cancellation)
    {
        var fixture = JsonSerializer.Deserialize<Fixture>(await File.ReadAllTextAsync(path, cancellation))
            ?? throw new InvalidDataException($"Fixture '{path}' is empty.");
        if (fixture.Frames is < 1 or > CaptureOptions.MaximumFrames)
            throw new InvalidDataException($"Fixture '{path}' has {fixture.Frames} frames; expected 1 to {CaptureOptions.MaximumFrames}.");
        if (new[] { fixture.RgbaPath, fixture.SourceVideo, fixture.FlowPath, fixture.SamPath, fixture.MhrPath, fixture.OmegaPath,
                fixture.DetectorPath, fixture.SmplPath, fixture.RigDirectory }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Fixture '{path}' is missing a path.");
        return fixture;
    }

    internal CaptureModels Models => new(FlowPath, SamPath, MhrPath, OmegaPath, DetectorPath, SmplPath);
}

/// <summary>Random access to a raw RGBA8 file holding exactly Frames × Width × Height pixels.</summary>
internal sealed class RawFrames : ICaptureFrames, IDisposable
{
    const long MaximumPixels = 33_177_600; // 8K, the pipeline's limit
    readonly FileStream stream;
    readonly int bytes;
    public int Width { get; }
    public int Height { get; }
    public int Count { get; }

    /// <summary>Opens the file. Throws when dimensions are invalid or its length disagrees.</summary>
    internal RawFrames(string path, int width, int height, int count)
    {
        if (width < 1 || height < 1 || (long)width * height > MaximumPixels || count < 1)
            throw new ArgumentException($"Invalid RGBA frame dimensions {width}x{height}x{count}.");
        Width = width; Height = height; Count = count; bytes = checked(width * height * 4);
        stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.RandomAccess);
        var length = stream.Length;
        if (length != (long)bytes * count)
        {
            stream.Dispose();
            throw new InvalidDataException($"'{path}' has {length} bytes; {count} frames of {width}x{height} RGBA need {(long)bytes * count}.");
        }
    }

    public async ValueTask ReadAsync(int frame, Memory<byte> rgba, CancellationToken cancellation)
    {
        if ((uint)frame >= (uint)Count || rgba.Length != bytes) throw new ArgumentException("Invalid frame index or destination size.");
        cancellation.ThrowIfCancellationRequested();
        stream.Position = (long)frame * bytes;
        await stream.ReadExactlyAsync(rgba, cancellation);
    }

    public void Dispose() => stream.Dispose();
}
