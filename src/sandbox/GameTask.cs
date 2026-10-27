using System.Runtime.CompilerServices;
using HumanoidMocapWorker;

namespace Sandbox;

/// <summary>s&amp;box task helpers used by the capture pipeline.</summary>
public static class GameTask
{
    /// <summary>Continues on the main thread; completes at once when already on it, as in the engine.</summary>
    public static MainThreadAwaitable MainThread() => default;
}

/// <summary>Awaitable returned by <see cref="GameTask.MainThread()"/>.</summary>
public readonly struct MainThreadAwaitable : INotifyCompletion
{
    public MainThreadAwaitable GetAwaiter() => this;
    public bool IsCompleted => MainLoop.IsMainThread;
    public void OnCompleted(Action continuation) => MainLoop.Enqueue(continuation);
    public void GetResult() { }
}

/// <summary>Main-thread checks.</summary>
public static class ThreadSafe
{
    public static bool IsMainThread => MainLoop.IsMainThread;

    public static void AssertIsMainThread()
    {
        if (!MainLoop.IsMainThread) throw new InvalidOperationException("This must be called on the main thread.");
    }
}
