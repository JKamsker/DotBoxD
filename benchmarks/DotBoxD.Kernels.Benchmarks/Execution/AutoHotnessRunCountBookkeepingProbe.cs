using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Hosting.Internal;

namespace DotBoxD.Kernels.Benchmarks.Execution;

using DotBoxD.Kernels;

internal static class AutoHotnessRunCountBookkeepingProbe
{
    private const string Entrypoint = "ShouldHandle";
    private const int WarmupIterations = 10_000;
    private const int Iterations = 500_000;
    private static readonly TimeSpan Elapsed = TimeSpan.FromTicks(37);

    public static void WriteResults(
        ExecutionPlan plan,
        SandboxExecutionResult interpreted,
        SandboxExecutionResult compiled)
    {
        Write("table built-in interpreted", MeasureTable(plan, interpreted, interpreted));
        Write("state built-in interpreted", MeasureState(plan, interpreted, interpreted));
        Write("table built-in warmed compiled", MeasureTable(plan, interpreted, compiled));
        Write("state built-in warmed compiled", MeasureState(plan, interpreted, compiled));
    }

    private static Measurement MeasureTable(
        ExecutionPlan plan,
        SandboxExecutionResult interpreted,
        SandboxExecutionResult result)
    {
        var hotness = new AutoExecutionHotness(maxEntries: 16);
        var options = Options(result.ActualMode);
        var seededRuns = SeedIfCompiled(hotness, plan, interpreted, result.ActualMode);
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = Run(hotness, plan, options, result);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(hotness, plan, options, result);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ValidateChecksum(checksum, seededRuns);
        Validate(
            hotness.BeginAttempt(plan, Entrypoint).Stats,
            seededRuns + WarmupIterations + Iterations,
            result);
        if (hotness.Count != 1)
        {
            throw new InvalidOperationException($"Expected one hotness entry, observed {hotness.Count}.");
        }

        return new Measurement(elapsed, allocated, checksum);
    }

    private static Measurement MeasureState(
        ExecutionPlan plan,
        SandboxExecutionResult interpreted,
        SandboxExecutionResult result)
    {
        var state = new AutoHotnessState(plan.PlanHash, Entrypoint);
        var options = Options(result.ActualMode);
        var seededRuns = SeedIfCompiled(state, interpreted, result.ActualMode);
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = Run(state, options, result);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(state, options, result);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ValidateChecksum(checksum, seededRuns);
        Validate(
            state.BeginAttempt().Stats,
            seededRuns + WarmupIterations + Iterations,
            result);
        return new Measurement(elapsed, allocated, checksum);
    }

    private static int Run(
        AutoExecutionHotness hotness,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxExecutionResult result)
    {
        var attempt = hotness.BeginRunCountAttempt(plan, Entrypoint);
        ValidateDecision(options, attempt.RunCount, result.ActualMode);
        attempt.Complete(result, Elapsed);
        return attempt.RunCount;
    }

    private static int Run(
        AutoHotnessState state,
        SandboxExecutionOptions options,
        SandboxExecutionResult result)
    {
        var attempt = state.BeginRunCountAttempt();
        ValidateDecision(options, attempt.RunCount, result.ActualMode);
        attempt.Complete(result, Elapsed);
        return attempt.RunCount;
    }

    private static void ValidateDecision(
        SandboxExecutionOptions options,
        int runCount,
        ExecutionMode expectedMode)
    {
        var mode = HotnessExecutionModeSelector.ChooseMode(options, runCount);
        if (mode != expectedMode)
        {
            throw new InvalidOperationException($"Expected {expectedMode} selection, observed {mode}.");
        }
    }

    private static int SeedIfCompiled(
        AutoExecutionHotness hotness,
        ExecutionPlan plan,
        SandboxExecutionResult interpreted,
        ExecutionMode expectedMode)
    {
        if (expectedMode != ExecutionMode.Compiled)
        {
            return 0;
        }

        hotness.BeginRunCountAttempt(plan, Entrypoint).Complete(interpreted, Elapsed);
        return 1;
    }

    private static int SeedIfCompiled(
        AutoHotnessState state,
        SandboxExecutionResult interpreted,
        ExecutionMode expectedMode)
    {
        if (expectedMode != ExecutionMode.Compiled)
        {
            return 0;
        }

        state.BeginRunCountAttempt().Complete(interpreted, Elapsed);
        return 1;
    }

    private static void Validate(
        ModuleHotnessStats stats,
        int completedRuns,
        SandboxExecutionResult result)
    {
        if (stats.PlanHash.Length != 64 ||
            stats.Entrypoint != Entrypoint ||
            stats.RunCount != completedRuns + 1 ||
            stats.CompletedRunCount != completedRuns ||
            stats.AverageFuelUsed != 7 ||
            stats.AverageInterpretedDuration != Elapsed ||
            stats.LastRunAt is null ||
            stats.CompileFailures != 0 ||
            stats.LastCompiledArtifactHash != result.ArtifactHash)
        {
            throw new InvalidOperationException("Built-in auto hotness bookkeeping invariants changed.");
        }
    }

    private static void ValidateChecksum(long checksum, int seededRuns)
    {
        var firstMeasuredRun = seededRuns + WarmupIterations + 1L;
        var lastMeasuredRun = seededRuns + WarmupIterations + Iterations;
        var expected = Iterations * (firstMeasuredRun + lastMeasuredRun) / 2;
        if (checksum != expected)
        {
            throw new InvalidOperationException($"Expected checksum {expected}, observed {checksum}.");
        }
    }

    private static SandboxExecutionOptions Options(ExecutionMode expectedMode)
        => new()
        {
            Mode = ExecutionMode.Auto,
            AutoCompileThreshold = expectedMode == ExecutionMode.Compiled ? 2 : int.MaxValue
        };

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-35} {measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.BytesPerOperation,10:N1} {measurement.Checksum,14:N0}");

    private readonly record struct Measurement(TimeSpan ElapsedTime, long AllocatedBytes, long Checksum)
    {
        public double NanosecondsPerOperation => ElapsedTime.TotalNanoseconds / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}
