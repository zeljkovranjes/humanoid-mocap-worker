using HumanoidMocapWorker;

namespace Sandbox;

/// <summary>The part of s&amp;box's graphics API the capture pipeline uses.</summary>
public static class Graphics
{
    /// <summary>True once the worker has opened a GPU.</summary>
    public static bool IsAvailable => GpuRuntime.Current is not null;

    /// <summary>True while a tensor batch is recording.</summary>
    public static bool IsActive => GpuRuntime.Current?.Active is not null;

    /// <summary>The driver's device-local memory budget in bytes, or 0 when unknown.</summary>
    public static ulong VideoMemoryBudget => GpuRuntime.Current?.Device.Budget().Budget ?? 0;

    /// <summary>Device-local memory this process uses, in bytes, or 0 when unknown.</summary>
    public static ulong VideoMemoryUsed => GpuRuntime.Current?.Device.Budget().Used ?? 0;

    // Every recorded command already ends with a full memory barrier.
    public static void UavBarrier(GpuBuffer buffer) { }
}
