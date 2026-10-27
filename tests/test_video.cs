using System.Diagnostics;
using HumanoidMocapWorker;

static class VideoTests
{
    const int Width = 64, Height = 48, FrameBytes = Width * Height * 4;

    // The worker applies the editor's timestamp rule to FFmpeg frames; on 24 fps sources it must
    // pick the same frames as FFmpeg's fps=30:round=up, which produced the validation fixtures.
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "worker-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var clip = Path.Combine(directory, "clip.mp4");
            if (await FfmpegAsync(["-f", "lavfi", "-i", $"testsrc2=size={Width}x{Height}:rate=24", "-t", "3", "-c:v", "mpeg4", "-q:v", "2", clip]) is null)
                throw new SkipException("ffmpeg is not installed");
            var reference = await FfmpegAsync(["-i", clip, "-vf", "fps=30:round=up", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"]);
            Check.That(reference.Length >= 90 * FrameBytes, "The reference decode is shorter than three seconds");
            await Compare(clip, 0, null, reference, 0, 90, directory);
            // Starts past the seek margin use FFmpeg's input seeking.
            await Compare(clip, 1.5, 2.5, reference, 45, 30, directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    static async Task Compare(string clip, double start, double? end, byte[] reference, int first, int count, string cache)
    {
        using var frames = await VideoCapture.DecodeAsync(clip, start, end, cache, _ => { }, default);
        Check.That(frames.Count == count && frames.Width == Width && frames.Height == Height, $"Range {start}–{end} decoded {frames.Count} frames");
        var pixels = new byte[FrameBytes];
        for (var frame = 0; frame < count; frame++)
        {
            await frames.ReadAsync(frame, pixels, default);
            Check.That(pixels.AsSpan().SequenceEqual(reference.AsSpan((first + frame) * FrameBytes, FrameBytes)),
                $"Timeline frame {frame} of range {start}–{end} differs from FFmpeg's fps=30:round=up frame {first + frame}");
        }
    }

    static async Task<byte[]> FfmpegAsync(string[] arguments)
    {
        var info = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-hide_banner", "-nostdin", "-v", "error", "-y" }.Concat(arguments)) info.ArgumentList.Add(argument);
        Process process;
        try { process = Process.Start(info); }
        catch (System.ComponentModel.Win32Exception) { return null; }
        using (process)
        {
            using var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
            var error = await process.StandardError.ReadToEndAsync();
            await copy; await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new Exception("ffmpeg failed: " + error);
            return output.ToArray();
        }
    }
}
