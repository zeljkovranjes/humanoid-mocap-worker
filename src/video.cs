using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;

namespace HumanoidMocapWorker;

/// <summary>Decodes a video's first video stream with the FFmpeg command-line tools into
/// timestamped RGBA8 frames, in presentation order. Times are seconds from the stream start.
/// Dispose stops the decoder.</summary>
internal sealed class FfmpegVideo : IDisposable
{
    const string Decoder = "ffmpeg", Probe = "ffprobe";
    // Decoding starts this far before the range so the frame displayed at its start is included.
    const double SeekMargin = 1;
    readonly Process process;
    readonly Channel<(double Time, int Width, int Height)> frames = Channel.CreateUnbounded<(double, int, int)>();
    readonly Queue<string> errors = new();
    readonly Task stderr;
    readonly double origin;

    FfmpegVideo(string path, double start, double origin)
    {
        this.origin = origin;
        var info = new ProcessStartInfo(Decoder) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-hide_banner", "-nostdin", "-loglevel", "info" }) info.ArgumentList.Add(argument);
        if (start > SeekMargin)
            foreach (var argument in new[] { "-ss", (start - SeekMargin).ToString("R", CultureInfo.InvariantCulture) }) info.ArgumentList.Add(argument);
        // showinfo reports each frame's timestamp and size just before its pixels are written.
        foreach (var argument in new[] { "-copyts", "-i", path, "-map", "0:v:0", "-vf", "showinfo", "-fps_mode", "passthrough",
            "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1" }) info.ArgumentList.Add(argument);
        process = Process.Start(info) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        stderr = Task.Run(ReadLog);
    }

    /// <summary>Starts decoding at or before <paramref name="start"/> seconds. Throws when
    /// FFmpeg is not installed or the file has no video stream.</summary>
    internal static async Task<(FfmpegVideo Video, double Duration)> OpenAsync(string path, double start, CancellationToken cancellation)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Video not found.", path);
        var probe = await RunProbeAsync(path, cancellation);
        return (new FfmpegVideo(path, start, probe.Start), probe.Duration);
    }

    /// <summary>The container duration in seconds, as the capture timeline uses it.</summary>
    internal static async Task<double> DurationAsync(string path, CancellationToken cancellation)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Video not found.", path);
        return (await RunProbeAsync(path, cancellation)).Duration;
    }

    /// <summary>The next decoded frame, or null at the end of the stream. Throws when FFmpeg fails.</summary>
    internal DecodedVideoFrame Read(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!frames.Reader.WaitToReadAsync(cancellation).AsTask().GetAwaiter().GetResult())
        {
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidDataException("ffmpeg failed: " + string.Join(" | ", errors));
            return null;
        }
        frames.Reader.TryRead(out var frame);
        // Each frame is a new array: the timeline builder keeps the current and next frames alive.
        var rgba = new byte[checked(frame.Width * frame.Height * 4)];
        process.StandardOutput.BaseStream.ReadExactly(rgba);
        return new DecodedVideoFrame(rgba, frame.Width, frame.Height, frame.Time - origin);
    }

    async Task ReadLog()
    {
        try
        {
            var (numerator, denominator) = (0L, 0L);
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (!line.Contains("Parsed_showinfo", StringComparison.Ordinal))
                {
                    errors.Enqueue(line);
                    if (errors.Count > 8) errors.Dequeue();
                }
                else if (Field(line, "config in time_base:") is { } timeBase) (numerator, denominator) = Rational(timeBase);
                else if (Field(line, " pts:") is { } pts && Field(line, " s:") is { } size)
                {
                    // Exact integer timestamps: pts_time is printed with only six decimals.
                    if (denominator <= 0) throw new InvalidDataException("ffmpeg reported a frame before its time base.");
                    var separator = size.IndexOf('x');
                    frames.Writer.TryWrite((long.Parse(pts, CultureInfo.InvariantCulture) * (double)numerator / denominator,
                        int.Parse(size[..separator], CultureInfo.InvariantCulture), int.Parse(size[(separator + 1)..], CultureInfo.InvariantCulture)));
                }
            }
            frames.Writer.TryComplete();
        }
        catch (Exception error) { frames.Writer.TryComplete(error); }
    }

    static (long Numerator, long Denominator) Rational(string text)
    {
        var parts = text.TrimEnd(',').Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator)
            || !long.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator))
            throw new InvalidDataException($"Unreadable time base '{text}'.");
        return (numerator, denominator);
    }

    static string Field(string line, string name)
    {
        var at = line.IndexOf(name, StringComparison.Ordinal);
        if (at < 0) return null;
        var start = at + name.Length;
        while (start < line.Length && line[start] == ' ') start++;
        var end = line.IndexOf(' ', start);
        return line[start..(end < 0 ? line.Length : end)];
    }

    static async Task<(double Start, double Duration)> RunProbeAsync(string path, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(Probe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=start_pts,time_base:format=duration",
            "-of", "default=noprint_wrappers=1", path }) info.ArgumentList.Add(argument);
        using var probe = Process.Start(info) ?? throw new InvalidOperationException("Could not start ffprobe.");
        var output = probe.StandardOutput.ReadToEndAsync(cancellation);
        var error = await probe.StandardError.ReadToEndAsync(cancellation);
        await probe.WaitForExitAsync(cancellation);
        var values = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1]);
        if (probe.ExitCode != 0 || !values.TryGetValue("duration", out var duration) || !values.TryGetValue("time_base", out var timeBase))
            throw new InvalidDataException($"ffprobe found no video stream with a duration in '{path}': {error.Trim()}");
        var (numerator, denominator) = Rational(timeBase);
        var start = long.TryParse(values.GetValueOrDefault("start_pts"), CultureInfo.InvariantCulture, out var pts) ? pts * (double)numerator / denominator : 0;
        return (start, double.Parse(duration, CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        stderr.Wait();
        process.Dispose();
    }
}
