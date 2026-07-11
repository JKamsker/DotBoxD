using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Model;

internal static class ResourceMeterDeadline
{
    public static long Create(ResourceLimits limits)
    {
        var now = Stopwatch.GetTimestamp();
        var timeoutTicks = Math.Ceiling(limits.EffectiveWallTime.TotalSeconds * Stopwatch.Frequency);
        var cappedTicks = timeoutTicks >= long.MaxValue - now
            ? long.MaxValue - now
            : (long)timeoutTicks;
        return now + Math.Max(1, cappedTicks);
    }

    public static void ThrowIfElapsed(long deadline)
    {
        if (Stopwatch.GetTimestamp() >= deadline)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.Timeout, "wall-time budget exhausted"));
        }
    }

    public static TimeSpan RemainingWallTime(long deadline)
    {
        var stopwatchTicks = deadline - Stopwatch.GetTimestamp();
        if (stopwatchTicks <= 0)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.Timeout, "wall-time budget exhausted"));
        }

        var timespanTicks = Math.Ceiling(stopwatchTicks / (double)Stopwatch.Frequency * TimeSpan.TicksPerSecond);
        if (timespanTicks >= TimeSpan.MaxValue.Ticks)
        {
            return TimeSpan.MaxValue;
        }

        return TimeSpan.FromTicks(Math.Max(1L, (long)timespanTicks));
    }
}
