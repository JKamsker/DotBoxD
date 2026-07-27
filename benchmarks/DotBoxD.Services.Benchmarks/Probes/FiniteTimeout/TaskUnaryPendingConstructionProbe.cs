using System.Diagnostics;
using DotBoxD.Services.Client;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TaskUnaryPendingConstructionProbe
{
    private const int WarmupIterations = 10_000;
    private const int MeasurementIterations = 1_000_000;
    private const string Service = "Probe";
    private const string Method = "Unary";
    private const string TimeoutOnlyTypeName =
        "DotBoxD.Services.Client.TimeoutOnlyPendingUnaryResponse`1";

    public static void Run()
    {
        using var liveCancellation = new CancellationTokenSource();
        var measurements = new[]
        {
            Measure(
                "finite/default",
                captureCallerCancellation: false,
                captureTimeoutTarget: true,
                CancellationToken.None),
            Measure(
                "finite/live",
                captureCallerCancellation: true,
                captureTimeoutTarget: true,
                liveCancellation.Token),
            Measure(
                "infinite/default",
                captureCallerCancellation: false,
                captureTimeoutTarget: false,
                CancellationToken.None),
            Measure(
                "infinite/live",
                captureCallerCancellation: true,
                captureTimeoutTarget: false,
                liveCancellation.Token),
        };

        Console.WriteLine("Task unary pending construction probe");
        Console.WriteLine("case                 total ms       ns/op    allocated B      B/op");
        foreach (var measurement in measurements)
        {
            Write(measurement);
        }
    }

    private static Measurement Measure(
        string name,
        bool captureCallerCancellation,
        bool captureTimeoutTarget,
        CancellationToken callerToken)
    {
        using var requests = new PendingRequests();
        AssertSelectedType(
            requests,
            captureCallerCancellation,
            captureTimeoutTarget,
            callerToken);

        RunIterations(
            requests,
            WarmupIterations,
            captureCallerCancellation,
            captureTimeoutTarget,
            callerToken);

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        var checksum = RunIterations(
            requests,
            MeasurementIterations,
            captureCallerCancellation,
            captureTimeoutTarget,
            callerToken);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var expectedChecksum =
            (long)MeasurementIterations * (MeasurementIterations + 1) / 2 +
            MeasurementIterations;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"pending checksum changed: expected {expectedChecksum}, got {checksum}");
        }

        return new Measurement(name, elapsed.TotalMilliseconds, allocated);
    }

    private static long RunIterations(
        PendingRequests requests,
        int iterations,
        bool captureCallerCancellation,
        bool captureTimeoutTarget,
        CancellationToken callerToken)
    {
        long checksum = 0;
        for (var messageId = 1; messageId <= iterations; messageId++)
        {
            var pending = CreatePending(
                requests,
                messageId,
                captureCallerCancellation,
                captureTimeoutTarget,
                callerToken);
            checksum += pending.MessageId;
            checksum += pending.TimeoutDeadline == long.MaxValue ? 1 : 0;
            if (!requests.Remove(messageId, pending, consumed: true))
            {
                throw new InvalidOperationException("pending identity removal failed");
            }
        }

        return checksum;
    }

    private static PendingUnaryResponse<int> CreatePending(
        PendingRequests requests,
        int messageId,
        bool captureCallerCancellation,
        bool captureTimeoutTarget,
        CancellationToken callerToken)
    {
        if (!requests.TryAddUnary<int>(
                messageId,
                captureCallerCancellation,
                captureTimeoutTarget,
                callerToken,
                Service,
                Method,
                out var pending))
        {
            throw new InvalidOperationException("pending message id was not reserved");
        }

        return pending;
    }

    private static void AssertSelectedType(
        PendingRequests requests,
        bool captureCallerCancellation,
        bool captureTimeoutTarget,
        CancellationToken callerToken)
    {
        var pending = CreatePending(
            requests,
            messageId: 1,
            captureCallerCancellation,
            captureTimeoutTarget,
            callerToken);
        var actualType = pending.GetType().GetGenericTypeDefinition().FullName;
        var expectedType = GetExpectedTypeName(captureCallerCancellation, captureTimeoutTarget);
        if (!StringComparer.Ordinal.Equals(expectedType, actualType))
        {
            throw new InvalidOperationException(
                $"pending type changed: expected {expectedType}, got {actualType}");
        }

        if (!requests.Remove(pending.MessageId, pending, consumed: true))
        {
            throw new InvalidOperationException("type-check pending identity removal failed");
        }
    }

    private static string? GetExpectedTypeName(
        bool captureCallerCancellation,
        bool captureTimeoutTarget)
    {
        if (captureTimeoutTarget)
        {
            if (captureCallerCancellation)
            {
                return typeof(PendingUnaryResponseWithTimeout<>).FullName;
            }

            return typeof(PendingRequests).Assembly.GetType(TimeoutOnlyTypeName) is null
                ? typeof(PendingUnaryResponseWithTimeout<>).FullName
                : TimeoutOnlyTypeName;
        }

        return captureCallerCancellation
            ? typeof(CancellablePendingUnaryResponse<>).FullName
            : typeof(PendingUnaryResponse<>).FullName;
    }

    private static void Write(Measurement measurement)
    {
        var nanosecondsPerOperation =
            measurement.ElapsedMilliseconds * 1_000_000 / MeasurementIterations;
        var bytesPerOperation = measurement.AllocatedBytes / (double)MeasurementIterations;
        Console.WriteLine(
            $"{measurement.Name,-20} {measurement.ElapsedMilliseconds,8:N1} " +
            $"{nanosecondsPerOperation,11:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{bytesPerOperation,10:N1}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        string Name,
        double ElapsedMilliseconds,
        long AllocatedBytes);
}
