using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using HumanoidMocapX;
using HumanoidMocapX.EditorTools;
using Sandbox;

namespace HumanoidMocapWorker;

/// <summary>The worker's commands. Each returns a process exit code; failures throw.</summary>
internal static class Cmd
{
    // Progress lines are read by programs, not embedded in HTML, so keep text such as "·" readable.
    static readonly JsonSerializerOptions ProgressJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly string[] Artifacts = ["boxes.json", "rotations.json", "intrinsics.json", "features.json", "flow-motion.json",
        "capture.hmotion", "human.fbx", "citizen.fbx"];

    internal static Task<int> RunAsync(Options options, CancellationToken cancellation) => options.Command switch
    {
        "capture" => CaptureAsync(options, cancellation),
        "fixture" => FixtureAsync(options, cancellation),
        "decode" => DecodeAsync(options, cancellation),
        "probe" => ProbeAsync(options, cancellation),
        _ => ModelsAsync(options, cancellation),
    };

    /// <summary>Video to .hmotion. Progress goes to standard output as JSON lines.</summary>
    static async Task<int> CaptureAsync(Options options, CancellationToken cancellation)
    {
        options.Allow("start", "end", "model", "camera-interval", "lanes", "models", "cache");
        var request = new CaptureRequest(Path.GetFullPath(options.Positional[0]), Path.GetFullPath(options.Positional[1]), options.Start,
            options.End, options.Model, options.CameraInterval, options.Lanes, options.ModelDirectory, options.CacheDirectory);
        using var gpu = GpuRuntime.Start();
        Log.Info("GPU: " + gpu.Device.Name);
        await VideoCapture.RunAsync(request, Progress, cancellation);
        return 0;

        static void Progress(string message, double? fraction)
            => Console.Out.WriteLine(JsonSerializer.Serialize(new { message, fraction }, ProgressJson));
    }

    /// <summary>Predecoded fixture to the eight stage artifacts, then prints their SHA-256 values.</summary>
    static async Task<int> FixtureAsync(Options options, CancellationToken cancellation)
    {
        options.Allow();
        var fixture = await Fixture.LoadAsync(options.Positional[0], cancellation);
        var directory = Path.GetFullPath(options.Positional[1]);
        using var gpu = GpuRuntime.Start();
        Log.Info("GPU: " + gpu.Device.Name);
        var timer = Stopwatch.StartNew();
        var stages = await FixtureCapture.RunAsync(fixture, directory, message => Log.Info(message), cancellation);
        var hashes = new Dictionary<string, string>();
        foreach (var name in Artifacts)
            hashes[name] = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, name), cancellation)));
        var result = JsonSerializer.Serialize(new { Gpu = gpu.Device.Name, TotalSeconds = timer.Elapsed.TotalSeconds, StageSeconds = stages, Sha256 = hashes },
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), result, cancellation);
        Console.Out.WriteLine(result);
        return 0;
    }

    /// <summary>Video to the raw RGBA 30 fps timeline the capture would use.</summary>
    static async Task<int> DecodeAsync(Options options, CancellationToken cancellation)
    {
        options.Allow("start", "end", "cache");
        using var frames = await VideoCapture.DecodeAsync(Path.GetFullPath(options.Positional[0]), options.Start, options.End,
            options.CacheDirectory, message => Log.Info(message), cancellation);
        var pixels = new byte[checked(frames.Width * frames.Height * 4)];
        await using (var output = new FileStream(options.Positional[1], FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
            for (var frame = 0; frame < frames.Count; frame++)
            {
                await frames.ReadAsync(frame, pixels, cancellation);
                await output.WriteAsync(pixels, cancellation);
            }
        Console.Out.WriteLine(JsonSerializer.Serialize(new { frames = frames.Count, width = frames.Width, height = frames.Height }));
        return 0;
    }

    /// <summary>Prints {"duration": seconds}, so a host can cap a requested range to the video.</summary>
    static async Task<int> ProbeAsync(Options options, CancellationToken cancellation)
    {
        options.Allow();
        cancellation.ThrowIfCancellationRequested();
        double duration;
        using (var decoder = new FfmpegVideoDecoder(Path.GetFullPath(options.Positional[0]))) duration = decoder.Duration;
        Console.Out.WriteLine(JsonSerializer.Serialize(new { duration }));
        return 0;
    }

    /// <summary>Downloads and verifies every checkpoint capture needs, with their licenses.</summary>
    static async Task<int> ModelsAsync(Options options, CancellationToken cancellation)
    {
        options.Allow("models");
        using var http = new HttpClient();
        var store = new ModelStore(http, options.ModelDirectory);
        Action<string> progress = message => Log.Info(message);
        foreach (var model in new[] { FlowModel.Base, FlowModel.Latest }) await store.EnsureAsync(model, progress, cancellation);
        foreach (var model in new[] { PerceptionModel.Sam3dBody, PerceptionModel.VggtOmega, PerceptionModel.YoloXL })
            await store.EnsurePerceptionAsync(model, progress, cancellation);
        await store.EnsureSmplAsync(progress, cancellation);
        Log.Info("Models are installed in " + Path.GetFullPath(options.ModelDirectory));
        return 0;
    }
}
