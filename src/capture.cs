using System.Text.Json;
using HumanoidMocapX;
using HumanoidMocapX.EditorTools;
using HumanoidMocapX.Inference;

namespace HumanoidMocapWorker;

/// <summary>Captures one person's motion from a video file, as the editor's local capture
/// does: FFmpeg decoding onto the 30 fps timeline, the verified installed models, then a
/// .hmotion document. The output is written atomically.</summary>
internal static class VideoCapture
{
    /// <summary>Throws for invalid settings, missing or damaged models, undecodable video or
    /// GPU failures; no partial output is left behind.</summary>
    internal static async Task RunAsync(CaptureRequest request, Action<string, double?> progress, CancellationToken cancellation)
    {
        var options = new CaptureOptions { Model = request.Model, CameraInterval = request.CameraInterval, Lanes = request.Lanes };
        options.Validate(); new HostedCaptureOptions(options.Model, options.CameraInterval, request.Start, request.End).Validate();
        void Report(string message) => progress(message, null);
        var models = await ModelStore.InstalledAsync(request.ModelDirectory, options.Model, Report, cancellation);
        using var frames = await DecodeAsync(request.Video, request.Start, request.End, request.CacheDirectory, Report, cancellation);
        // FlowVideoProcessor.RunAsync's stages, with managed memory released between them.
        var processor = new FlowVideoProcessor(models, options, null, progress);
        // Later stages cover only the frames in which the person is visible.
        var person = await processor.DetectAsync(frames, cancellation); FixtureCapture.ReleaseMemory();
        var visible = person.Visible(frames);
        var camera = await processor.CameraAsync(visible, cancellation); FixtureCapture.ReleaseMemory();
        var (features, observations) = await processor.FeaturesAsync(visible, person.Boxes, camera.Intrinsics, cancellation); FixtureCapture.ReleaseMemory();
        var motion = await processor.GenerateAsync(features, camera.Rotations, visible.Count, cancellation);
        motion.Video = request.Video; motion.Name = Path.GetFileNameWithoutExtension(request.Video); motion.StartTime = request.Start + person.StartSeconds;
        var document = motion.ToDocument(); new ObservedDetail(observations, camera.Rotations).Apply(document);
        if (person.Note(request.Start, frames.Count) is { } note) document.Diagnostics.Add(note);
        var partial = request.Output + ".partial";
        try
        {
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                await JsonSerializer.SerializeAsync(output, document, MotionDocument.JsonOptions, cancellation);
            File.Move(partial, request.Output, overwrite: true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
        progress("Motion is ready", 1);
    }

    /// <summary>Decodes the selected range into a temporary 30 fps RGBA timeline under cache;
    /// the cache file is deleted when the result is disposed.</summary>
    internal static Task<VideoFrames> DecodeAsync(string video, double start, double? end, string cache,
        Action<string> progress, CancellationToken cancellation) => Task.Run(async () =>
    {
        progress("Opening video decoder…");
        using var decoder = new FfmpegVideoDecoder(video);
        decoder.Seek(start);
        return await VideoFrames.CreateAsync(decoder.Read, decoder.Duration, start, end, cache, progress, cancellation);
    }, cancellation);
}

/// <summary>Settings for one video capture. End null means the rest of the video.</summary>
internal sealed record CaptureRequest(string Video, string Output, double Start, double? End, FlowModel Model,
    int CameraInterval, int Lanes, string ModelDirectory, string CacheDirectory);
