using System.Collections.Concurrent;

namespace HumanoidMocapWorker;

/// <summary>The worker's "main thread": a single-threaded synchronization context that
/// runs the capture the way the editor's main thread does, so GPU recording and
/// main-thread assertions behave alike.</summary>
internal sealed class MainLoop : SynchronizationContext
{
    readonly BlockingCollection<(SendOrPostCallback Callback, object State)> queue = new();
    static Thread thread;
    static MainLoop current;

    internal static bool IsMainThread => thread is not null && Thread.CurrentThread == thread;

    /// <summary>Posts to the running loop. Throws when no loop is running.</summary>
    internal static void Enqueue(Action continuation)
        => (current ?? throw new InvalidOperationException("The main loop is not running.")).Post(static state => ((Action)state)(), continuation);

    public override void Post(SendOrPostCallback callback, object state) => queue.Add((callback, state));

    public override void Send(SendOrPostCallback callback, object state)
    {
        if (IsMainThread) { callback(state); return; }
        using var done = new ManualResetEventSlim();
        Exception failure = null;
        Post(_ => { try { callback(state); } catch (Exception error) { failure = error; } finally { done.Set(); } }, null);
        done.Wait();
        if (failure is not null) throw new AggregateException(failure);
    }

    /// <summary>Runs <paramref name="entry"/> on the calling thread as the main thread until
    /// its task completes, then returns its result. Exceptions propagate unwrapped.</summary>
    internal static T Run<T>(Func<Task<T>> entry)
    {
        if (current is not null) throw new InvalidOperationException("The main loop is already running.");
        var loop = new MainLoop();
        var previous = SynchronizationContext.Current;
        thread = Thread.CurrentThread; current = loop;
        SynchronizationContext.SetSynchronizationContext(loop);
        try
        {
            var task = entry();
            task.ContinueWith(_ => loop.queue.CompleteAdding(), TaskScheduler.Default);
            foreach (var (callback, state) in loop.queue.GetConsumingEnumerable()) callback(state);
            return task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            current = null; thread = null; loop.queue.Dispose();
        }
    }
}
