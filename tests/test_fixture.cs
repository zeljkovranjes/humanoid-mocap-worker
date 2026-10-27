using HumanoidMocapWorker;

static class FixtureTests
{
    internal static async Task Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "worker-frames-" + Guid.NewGuid().ToString("N") + ".rgba");
        try
        {
            var bytes = new byte[2 * 3 * 4 * 2];
            for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)index;
            await File.WriteAllBytesAsync(path, bytes);
            using (var frames = new RawFrames(path, 2, 3, 2))
            {
                var frame = new byte[24];
                await frames.ReadAsync(1, frame, default);
                Check.That(frame.SequenceEqual(bytes[24..]), "The second frame must come from its byte offset");
                await ThrowsAsync<ArgumentException>(() => frames.ReadAsync(2, frame, default).AsTask(), "Frames past the end must be rejected");
                await ThrowsAsync<ArgumentException>(() => frames.ReadAsync(0, new byte[23], default).AsTask(), "Short destinations must be rejected");
            }
            Check.Throws<InvalidDataException>(() => new RawFrames(path, 2, 3, 3), "A file shorter than its declared frames must be rejected");
            Check.Throws<ArgumentException>(() => new RawFrames(path, 0, 3, 2), "Empty dimensions must be rejected");
        }
        finally { File.Delete(path); }
    }

    static async Task ThrowsAsync<T>(Func<Task> action, string failure) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception(failure);
    }
}
