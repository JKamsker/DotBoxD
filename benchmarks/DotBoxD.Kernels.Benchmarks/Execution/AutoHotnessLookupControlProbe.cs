using System.Diagnostics;
using DotBoxD.Hosting.Internal;

namespace DotBoxD.Kernels.Benchmarks.Execution;

using DotBoxD.Kernels;

internal static class AutoHotnessLookupControlProbe
{
    private const string Entrypoint = "ShouldHandle";
    private const int WarmupIterations = 10_000;
    private const int Iterations = 500_000;
    private const int ConcurrentWorkers = 4;
    private const int ConcurrentWarmupPerWorker = 10_000;
    private const int ConcurrentIterationsPerWorker = 250_000;
    private const int ChurnIterations = 200_000;

    public static void WriteResults(ExecutionPlan plan)
    {
        Write("table equal distinct refs", MeasureDistinctReferences(plan));
        Write("table capacity-two churn", MeasureCapacityChurn(plan));
        Write("table four-worker exact refs", MeasureConcurrent(plan, useTable: true));
        Write("state four-worker control", MeasureConcurrent(plan, useTable: false));
    }

    private static Measurement MeasureDistinctReferences(ExecutionPlan plan)
    {
        var hotness = new AutoExecutionHotness(maxEntries: 16);
        var distinctEntrypoint = new string(Entrypoint.ToCharArray());
        if (ReferenceEquals(distinctEntrypoint, Entrypoint))
        {
            throw new InvalidOperationException("Distinct-reference control reused the entrypoint instance.");
        }

        _ = hotness.BeginRunCountAttempt(plan, Entrypoint);
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = hotness.BeginRunCountAttempt(plan, distinctEntrypoint);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += hotness.BeginRunCountAttempt(plan, distinctEntrypoint).RunCount;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var first = WarmupIterations + 2L;
        var last = WarmupIterations + Iterations + 1L;
        ValidateChecksum(checksum, Iterations * (first + last) / 2);
        var snapshot = hotness.BeginAttempt(plan, Entrypoint).Stats;
        if (snapshot.RunCount != WarmupIterations + Iterations + 2 || hotness.Count != 1)
        {
            throw new InvalidOperationException("Distinct-reference history diverged.");
        }

        return new Measurement(elapsed, allocated, checksum, Iterations);
    }

    private static Measurement MeasureCapacityChurn(ExecutionPlan plan)
    {
        var hotness = new AutoExecutionHotness(maxEntries: 2);
        var entrypoints = new[] { "capacity-a", "capacity-b", "capacity-c" };
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = hotness.BeginRunCountAttempt(plan, entrypoints[i % entrypoints.Length]);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var i = 0; i < ChurnIterations; i++)
        {
            var entrypoint = entrypoints[(WarmupIterations + i) % entrypoints.Length];
            checksum += hotness.BeginRunCountAttempt(plan, entrypoint).RunCount;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ValidateChecksum(checksum, ChurnIterations);
        if (hotness.Count != 2)
        {
            throw new InvalidOperationException("Capacity churn escaped its two-entry bound.");
        }

        return new Measurement(elapsed, allocated, checksum, ChurnIterations);
    }

    private static Measurement MeasureConcurrent(ExecutionPlan plan, bool useTable)
    {
        var hotness = useTable ? new AutoExecutionHotness(maxEntries: 16) : null;
        var state = useTable ? null : new AutoHotnessState(plan.PlanHash, Entrypoint);
        var checksums = new long[ConcurrentWorkers];
        var allocations = new long[ConcurrentWorkers];
        using var ready = new CountdownEvent(ConcurrentWorkers);
        using var start = new ManualResetEventSlim();
        using var finished = new CountdownEvent(ConcurrentWorkers);
        var threads = new Thread[ConcurrentWorkers];
        for (var worker = 0; worker < threads.Length; worker++)
        {
            var workerIndex = worker;
            threads[worker] = new Thread(() => RunConcurrentWorker(
                plan,
                hotness,
                state,
                ready,
                start,
                finished,
                checksums,
                allocations,
                workerIndex));
            threads[worker].Start();
        }

        ready.Wait();
        var started = Stopwatch.GetTimestamp();
        start.Set();
        finished.Wait();
        var elapsed = Stopwatch.GetElapsedTime(started);
        foreach (var thread in threads)
        {
            thread.Join();
        }

        var operations = ConcurrentWorkers * ConcurrentIterationsPerWorker;
        var warmups = ConcurrentWorkers * ConcurrentWarmupPerWorker;
        var checksum = checksums.Sum();
        var expectedChecksum = operations * (2L * warmups + operations + 1) / 2;
        ValidateChecksum(checksum, expectedChecksum);
        var expectedRunCount = warmups + operations + 1;
        if (useTable)
        {
            var snapshot = hotness!.BeginAttempt(plan, Entrypoint).Stats;
            if (snapshot.RunCount != expectedRunCount || hotness.Count != 1)
            {
                throw new InvalidOperationException("Concurrent table history diverged.");
            }
        }
        else if (state!.BeginAttempt().Stats.RunCount != expectedRunCount)
        {
            throw new InvalidOperationException("Concurrent state history diverged.");
        }

        return new Measurement(elapsed, allocations.Sum(), checksum, operations);
    }

    private static void RunConcurrentWorker(
        ExecutionPlan plan,
        AutoExecutionHotness? hotness,
        AutoHotnessState? state,
        CountdownEvent ready,
        ManualResetEventSlim start,
        CountdownEvent finished,
        long[] checksums,
        long[] allocations,
        int workerIndex)
    {
        for (var i = 0; i < ConcurrentWarmupPerWorker; i++)
        {
            _ = Begin(hotness, state, plan);
        }

        ready.Signal();
        start.Wait();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (var i = 0; i < ConcurrentIterationsPerWorker; i++)
        {
            checksum += Begin(hotness, state, plan);
        }

        checksums[workerIndex] = checksum;
        allocations[workerIndex] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        finished.Signal();
    }

    private static int Begin(
        AutoExecutionHotness? hotness,
        AutoHotnessState? state,
        ExecutionPlan plan)
        => hotness is null
            ? state!.BeginRunCountAttempt().RunCount
            : hotness.BeginRunCountAttempt(plan, Entrypoint).RunCount;

    private static void ValidateChecksum(long actual, long expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"Expected checksum {expected}, observed {actual}.");
        }
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-35} {measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.BytesPerOperation,10:N1} {measurement.Checksum,14:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        TimeSpan ElapsedTime,
        long AllocatedBytes,
        long Checksum,
        int Operations)
    {
        public double NanosecondsPerOperation => ElapsedTime.TotalNanoseconds / Operations;

        public double BytesPerOperation => AllocatedBytes / (double)Operations;
    }
}
