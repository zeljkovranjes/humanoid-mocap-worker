namespace Sandbox
{
    /// <summary>Engine logging, written to standard error so standard output stays machine-readable.</summary>
    public static class Log
    {
        public static void Info(object message) => Console.Error.WriteLine(message);
        public static void Warning(object message) => Console.Error.WriteLine("warning: " + message);
        public static void Error(object message) => Console.Error.WriteLine("error: " + message);
    }
}

namespace Sandbox.Engine
{
    /// <summary>Hardware facts the capture pipeline reads.</summary>
    public static class SystemInfo
    {
        /// <summary>Size of the GPU's device-local memory in bytes, or 0 without a GPU.</summary>
        public static ulong GpuMemory => HumanoidMocapWorker.GpuRuntime.Current?.Device.TotalMemory ?? 0;
    }
}
