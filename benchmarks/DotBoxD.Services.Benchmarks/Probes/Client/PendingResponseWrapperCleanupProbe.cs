using System.Diagnostics;
using DotBoxD.Services.Client;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PendingResponseWrapperCleanupProbe
{
    private const int WarmupIterations = 20_000;
    private const int MeasurementIterations = 500_000;
    private const int LockedMissIterations = 5_000_000;

    public static void Run()
    {
        using var pending = new PendingResponseWrapperHarness(forcePendingSend: true);
        var pendingUnary = Measure(
            "pending send, ValueTask<T>",
            pending,
            PendingResponseShape.Unary);
        var pendingNoResponse = Measure(
            "pending send, ValueTask",
            pending,
            PendingResponseShape.NoResponse);
        pending.VerifyTotals(CallsPerShape);

        using var synchronous = new PendingResponseWrapperHarness(forcePendingSend: false);
        var synchronousUnary = Measure(
            "synchronous send, ValueTask<T>",
            synchronous,
            PendingResponseShape.Unary);
        var synchronousNoResponse = Measure(
            "synchronous send, ValueTask",
            synchronous,
            PendingResponseShape.NoResponse);
        synchronous.VerifyTotals(CallsPerShape);

        var lockedMiss = MeasureLockedMiss();

        Console.WriteLine("Pending-response wrapper cleanup probe");
        Console.WriteLine(
            $"iterations = {MeasurementIterations:N0}; warmup = {WarmupIterations:N0}; " +
            $"max pending requests = {PendingResponseWrapperHarness.MaxPendingRequests}");
        Console.WriteLine("case                                  total ms       ns/op    allocated B      B/op");
        Write(pendingUnary);
        Write(pendingNoResponse);
        Write(synchronousUnary);
        Write(synchronousNoResponse);
        Write(lockedMiss);
        Console.WriteLine(
            $"validated per harness: calls = {pending.InvocationCount:N0}; " +
            $"result checksum = {pending.ResultChecksum}; " +
            $"message-id checksum = {pending.MessageIdChecksum}");
        Console.WriteLine(
            $"pending source reuse: one source, {pending.SourceReuseCycles:N0} consumed generations; " +
            $"accepted responses = {pending.AcceptedResponses:N0}; follow-ups = {pending.FollowUpCalls:N0}");
        Console.WriteLine(
            $"synchronous control: accepted responses = {synchronous.AcceptedResponses:N0}; " +
            $"follow-ups = {synchronous.FollowUpCalls:N0}");
    }

    private static long CallsPerShape =>
        WarmupIterations + MeasurementIterations + 1L;

    private static Measurement Measure(
        string name,
        PendingResponseWrapperHarness harness,
        PendingResponseShape shape)
    {
        for (var iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = harness.InvokeOnce(shape);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var iteration = 0; iteration < MeasurementIterations; iteration++)
        {
            checksum += harness.InvokeOnce(shape);
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var expectedChecksum = shape == PendingResponseShape.Unary
            ? (long)MeasurementIterations * PendingResponseWrapperHarness.ResponseValue
            : MeasurementIterations;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} checksum changed: expected {expectedChecksum}, got {checksum}.");
        }

        harness.VerifyFollowUpCapacity(shape);
        return new Measurement(name, MeasurementIterations, elapsed, allocated);
    }

    private static Measurement MeasureLockedMiss()
    {
        using var requests = new PendingRequests();
        if (!requests.TryAdd(1, out var pending) ||
            !requests.TryTake(1, out var removed) ||
            !ReferenceEquals(pending, removed))
        {
            throw new InvalidOperationException("Could not prepare the isolated pending-request miss.");
        }

        for (var iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = requests.Remove(1, pending, consumed: true);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        var unexpectedHits = 0;
        for (var iteration = 0; iteration < LockedMissIterations; iteration++)
        {
            if (requests.Remove(1, pending, consumed: true))
            {
                unexpectedHits++;
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (unexpectedHits != 0 || requests.Count != 0)
        {
            throw new InvalidOperationException(
                $"Locked-miss control changed state: hits={unexpectedHits}, count={requests.Count}.");
        }

        return new Measurement("isolated locked dictionary miss", LockedMissIterations, elapsed, allocated);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(Measurement measurement) =>
        Console.WriteLine(
            $"{measurement.Name,-37} {measurement.Elapsed.TotalMilliseconds,8:N1} " +
            $"{measurement.NanosecondsPerOperation,11:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.BytesPerOperation,10:N1}");

    private readonly record struct Measurement(
        string Name,
        int Operations,
        TimeSpan Elapsed,
        long AllocatedBytes)
    {
        public double NanosecondsPerOperation =>
            Elapsed.TotalMilliseconds * 1_000_000 / Operations;

        public double BytesPerOperation => AllocatedBytes / (double)Operations;
    }
}
