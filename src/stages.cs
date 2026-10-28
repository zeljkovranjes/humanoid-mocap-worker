using System.Diagnostics;
using System.Text.Json;
using HumanoidMocapX;
using HumanoidMocapX.EditorTools;
using HumanoidMocapX.Inference;

namespace HumanoidMocapWorker;

/// <summary>Runs a predecoded fixture through every model stage and saves each stage's
/// output exactly as the editor's ClipChecks does, so the files can be compared byte for byte.</summary>
internal static class FixtureCapture
{
    /// <summary>Writes boxes, rotations, intrinsics, features, flow-motion, capture.hmotion,
    /// human.fbx and citizen.fbx (plus timings) into a new directory. Returns stage seconds.</summary>
    internal static async Task<Dictionary<string, double>> RunAsync(Fixture fixture, string directory,
        Action<string> progress, CancellationToken cancellation)
    {
        if (Directory.Exists(directory)) throw new IOException($"'{directory}' exists; use a fresh directory so no evidence is overwritten.");
        Directory.CreateDirectory(directory);
        await Save("fixture.json", fixture);
        var options = new CaptureOptions { CameraInterval = fixture.CameraInterval, Model = fixture.Model };
        var processor = new FlowVideoProcessor(fixture.Models, options, progress);
        using var frames = new RawFrames(fixture.RgbaPath, fixture.Width, fixture.Height, fixture.Frames);
        var times = new Dictionary<string, double>();
        var timer = Stopwatch.StartNew();
        var person = await processor.DetectAsync(frames, cancellation);
        if (person.First != 0 || person.Count != frames.Count) throw new InvalidDataException("The validation fixture must show the person in every frame.");
        var boxes = person.Boxes;
        times["Detection"] = timer.Elapsed.TotalSeconds;
        await Save("boxes.json", boxes); await Save("timings.json", times); ReleaseMemory(); timer.Restart();
        var camera = await processor.CameraAsync(frames, cancellation);
        times["Camera"] = timer.Elapsed.TotalSeconds;
        await Save("rotations.json", camera.Rotations); await Save("intrinsics.json", camera.Intrinsics); await Save("timings.json", times); ReleaseMemory(); timer.Restart();
        var (features, observations) = await processor.FeaturesAsync(frames, boxes, camera.Intrinsics, cancellation);
        times["Features"] = timer.Elapsed.TotalSeconds;
        await Save("features.json", features); await Save("observations.json", observations); await Save("timings.json", times); ReleaseMemory(); timer.Restart();
        var motion = await processor.GenerateAsync(features, camera.Rotations, frames.Count, cancellation);
        times["Generation"] = timer.Elapsed.TotalSeconds; timer.Restart();
        var detail = new ObservedDetail(observations, camera.Rotations);
        await Task.Run(() => MotionExport.WriteAsync(motion, detail, fixture.SourceVideo, fixture.RigDirectory, directory, cancellation), cancellation);
        times["Export"] = timer.Elapsed.TotalSeconds;
        await Save("timings.json", times);
        return times;

        Task Save<T>(string name, T value) => File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value), cancellation);
    }

    /// <summary>Returns freed managed memory to the OS between stages. Each stage's CPU copy
    /// of its weights is garbage once uploaded, and the next stage loads gigabytes more;
    /// without this the 5 s clip peaked at 10.1 GB of RSS instead of 5.8 GB.</summary>
    internal static void ReleaseMemory() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
}
