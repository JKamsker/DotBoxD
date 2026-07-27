using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class FrameReadTimeoutSourceLifetimeProbe
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(1);
    private const int WarmupIterations = 100_000;
    private const int Iterations = 2_000_000;

    public static void Run()
    {
        WarmupConstruction();
        var construction = MeasureConstruction();

        using var cachedSource = new FrameReadTimeoutSource();
        var cachedToken = cachedSource.Start(CancellationToken.None, IdleTimeout);
        cachedSource.CancelPendingTimeout();
        WarmupCachedSource(cachedSource);
        ValidateCachedSource(cachedSource, cachedToken);
        var cached = MeasureCachedSource(cachedSource);
        ValidateCachedSource(cachedSource, cachedToken);
        WarmupInactiveCancel(cachedSource);
        var inactiveCancel = MeasureInactiveCancel(cachedSource);
        ValidateCachedSource(cachedSource, cachedToken);

        var disposedStartStatus = ObserveDisposedStartStatus();

        Console.WriteLine("Frame read timeout source lifetime probe");
        Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
        WriteResult("Construct + Dispose", construction);
        WriteResult("Cached Start + Cancel", cached);
        WriteResult("Cached inactive Cancel", inactiveCancel);
        Console.WriteLine($"Start after Dispose       {disposedStartStatus}");
    }

    private static void WarmupConstruction()
    {
        FrameReadTimeoutSource? last = null;
        for (var i = 0; i < WarmupIterations; i++)
        {
            last = CreateAndDispose();
        }

        GC.KeepAlive(last);
    }

    private static ProbeResult MeasureConstruction()
    {
        ForceGc();
        FrameReadTimeoutSource? last = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            last = CreateAndDispose();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(last);
        return new ProbeResult(elapsed, allocated);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FrameReadTimeoutSource CreateAndDispose()
    {
        var source = new FrameReadTimeoutSource();
        source.Dispose();
        return source;
    }

    private static void WarmupCachedSource(FrameReadTimeoutSource source)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            source.Start(CancellationToken.None, IdleTimeout);
            source.CancelPendingTimeout();
        }
    }

    private static ProbeResult MeasureCachedSource(FrameReadTimeoutSource source)
    {
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            source.Start(CancellationToken.None, IdleTimeout);
            source.CancelPendingTimeout();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new ProbeResult(elapsed, allocated);
    }

    private static void WarmupInactiveCancel(FrameReadTimeoutSource source)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            source.CancelPendingTimeout();
        }
    }

    private static ProbeResult MeasureInactiveCancel(FrameReadTimeoutSource source)
    {
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            source.CancelPendingTimeout();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new ProbeResult(elapsed, allocated);
    }

    private static void ValidateCachedSource(
        FrameReadTimeoutSource source,
        CancellationToken expectedToken)
    {
        var token = source.Start(CancellationToken.None, IdleTimeout);
        if (!token.Equals(expectedToken))
        {
            throw new InvalidOperationException("The warmed timeout source was unexpectedly replaced.");
        }

        source.CancelPendingTimeout();
        if (token.IsCancellationRequested)
        {
            throw new InvalidOperationException("CancelPendingTimeout canceled the frame owner token.");
        }
    }

    private static string ObserveDisposedStartStatus()
    {
        using var source = new FrameReadTimeoutSource();
        source.Dispose();

        try
        {
            var token = source.Start(CancellationToken.None, IdleTimeout);
            source.CancelPendingTimeout();
            return token.CanBeCanceled
                ? "accepted (cancelable source recreated)"
                : throw new InvalidOperationException("Start returned a non-cancelable timeout token.");
        }
        catch (ObjectDisposedException)
        {
            return "rejected (ObjectDisposedException)";
        }
    }

    private static void WriteResult(string name, ProbeResult result) =>
        Console.WriteLine(
            $"{name,-24} {result.Elapsed.TotalMilliseconds,8:N1} ms " +
            $"{result.Elapsed.TotalNanoseconds / Iterations,8:N1} ns/op " +
            $"{result.AllocatedBytes,12:N0} B " +
            $"{result.AllocatedBytes / (double)Iterations,8:N1} B/op");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct ProbeResult(TimeSpan Elapsed, long AllocatedBytes);
}
