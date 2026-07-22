using System.Diagnostics;
using DotBoxD.Services.Client;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PendingTimeoutSchedulerProbe
{
    private const int WarmupIterations = 100_000;
    private const int Iterations = 10_000_000;

    public static void Run()
    {
        var callbacks = 0;
        using var scheduler = new PendingRequestTimeoutScheduler(() => Interlocked.Increment(ref callbacks));
        var earliest = PendingRequestTimeoutScheduler.GetDeadline(TimeSpan.FromHours(1));
        scheduler.Schedule(earliest);
        for (var i = 0; i < WarmupIterations; i++)
        {
            scheduler.Schedule(earliest + i + 1L);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            scheduler.Schedule(earliest + i + 1L);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (Volatile.Read(ref callbacks) != 0)
        {
            throw new InvalidOperationException("The far-future timeout scheduler fired during the probe.");
        }

        Console.WriteLine("Pending request timeout scheduler probe");
        Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
        Console.WriteLine(
            $"Later deadline Schedule      {elapsed.TotalMilliseconds,8:N1} ms " +
            $"{elapsed.TotalNanoseconds / Iterations,8:N1} ns/op " +
            $"{allocated,12:N0} B {allocated / (double)Iterations,8:N1} B/op");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
