using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Hosting.Internal;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Execution;

internal static class AutoHotnessBookkeepingProbe
{
    private const string Entrypoint = "ShouldHandle";
    private const string ArtifactHash = "benchmark-artifact";
    private const int WarmupIterations = 10_000;
    private const int Iterations = 500_000;
    private static readonly TimeSpan Elapsed = TimeSpan.FromTicks(37);
    private static readonly HotnessExecutionModeSelector Selector = new();

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var plan = await PreparePlanAsync(host);
        var interpreted = Result(plan, ExecutionMode.Interpreted);
        var compiled = Result(plan, ExecutionMode.Compiled);

        Console.WriteLine("path                                  ns/op       B/op       checksum");
        Write("table interpreted", MeasureTable(plan, interpreted, ExecutionMode.Interpreted));
        Write("state interpreted", MeasureState(plan, interpreted, ExecutionMode.Interpreted));
        Write("table warmed compiled", MeasureTable(plan, compiled, ExecutionMode.Compiled));
        Write("state warmed compiled", MeasureState(plan, compiled, ExecutionMode.Compiled));
    }

    private static Measurement MeasureTable(
        ExecutionPlan plan,
        SandboxExecutionResult result,
        ExecutionMode expectedMode)
    {
        var hotness = new AutoExecutionHotness(maxEntries: 16);
        var options = Options(expectedMode);
        var completedBeforeWarmup = SeedIfCompiled(hotness, plan, expectedMode);
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = Run(hotness, plan, options, result, expectedMode);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(hotness, plan, options, result, expectedMode);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Validate(
            hotness.BeginAttempt(plan, Entrypoint).Stats,
            completedBeforeWarmup + WarmupIterations + Iterations,
            expectedMode);
        if (hotness.Count != 1)
        {
            throw new InvalidOperationException($"Expected one hotness entry, observed {hotness.Count}.");
        }

        return new Measurement(elapsed, allocated, checksum);
    }

    private static Measurement MeasureState(
        ExecutionPlan plan,
        SandboxExecutionResult result,
        ExecutionMode expectedMode)
    {
        var state = new AutoHotnessState(plan.PlanHash, Entrypoint);
        var options = Options(expectedMode);
        var completedBeforeWarmup = SeedIfCompiled(state, expectedMode);
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = Run(state, plan, options, result, expectedMode);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            checksum += Run(state, plan, options, result, expectedMode);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Validate(
            state.BeginAttempt().Stats,
            completedBeforeWarmup + WarmupIterations + Iterations,
            expectedMode);
        return new Measurement(elapsed, allocated, checksum);
    }

    private static int Run(
        AutoExecutionHotness hotness,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        ExecutionMode expectedMode)
    {
        var attempt = hotness.BeginAttempt(plan, Entrypoint);
        ValidateDecision(plan, options, attempt.Stats, expectedMode);
        attempt.Complete(result, Elapsed);
        return attempt.Stats.RunCount;
    }

    private static int Run(
        AutoHotnessState state,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        ExecutionMode expectedMode)
    {
        var attempt = state.BeginAttempt();
        ValidateDecision(plan, options, attempt.Stats, expectedMode);
        attempt.Complete(result, Elapsed);
        return attempt.Stats.RunCount;
    }

    private static void ValidateDecision(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats stats,
        ExecutionMode expectedMode)
    {
        var decision = Selector.Choose(plan, options, stats, CompiledCacheStatus.None);
        if (decision.Mode != expectedMode)
        {
            throw new InvalidOperationException(
                $"Expected {expectedMode} selection, observed {decision.Mode}.");
        }
    }

    private static int SeedIfCompiled(
        AutoExecutionHotness hotness,
        ExecutionPlan plan,
        ExecutionMode expectedMode)
    {
        if (expectedMode != ExecutionMode.Compiled)
        {
            return 0;
        }

        var seed = hotness.BeginAttempt(plan, Entrypoint);
        seed.Complete(Result(plan, ExecutionMode.Interpreted), Elapsed);
        return 1;
    }

    private static int SeedIfCompiled(AutoHotnessState state, ExecutionMode expectedMode)
    {
        if (expectedMode != ExecutionMode.Compiled)
        {
            return 0;
        }

        state.BeginAttempt().Complete(
            Result(state.PlanHash, ExecutionMode.Interpreted),
            Elapsed);
        return 1;
    }

    private static void Validate(ModuleHotnessStats stats, int completedRuns, ExecutionMode expectedMode)
    {
        if (stats.PlanHash.Length != 64 ||
            stats.Entrypoint != Entrypoint ||
            stats.RunCount != completedRuns + 1 ||
            stats.CompletedRunCount != completedRuns ||
            stats.AverageFuelUsed != 7 ||
            stats.AverageInterpretedDuration != Elapsed ||
            stats.LastRunAt is null ||
            stats.CompileFailures != 0 ||
            stats.LastCompiledArtifactHash != (expectedMode == ExecutionMode.Compiled ? ArtifactHash : null))
        {
            throw new InvalidOperationException("Auto hotness bookkeeping invariants changed.");
        }
    }

    private static SandboxExecutionOptions Options(ExecutionMode expectedMode)
        => new()
        {
            Mode = ExecutionMode.Auto,
            AutoCompileThreshold = expectedMode == ExecutionMode.Compiled ? 2 : int.MaxValue
        };

    private static SandboxExecutionResult Result(ExecutionPlan plan, ExecutionMode mode)
        => Result(plan.PlanHash, mode) with
        {
            ModuleHash = plan.ModuleHash,
            PolicyHash = plan.PolicyHash
        };

    private static SandboxExecutionResult Result(string planHash, ExecutionMode mode)
        => new()
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = new SandboxResourceUsage(7, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            AuditEvents = [],
            ActualMode = mode,
            ExecutionDispatched = true,
            ModuleHash = "benchmark-module",
            PlanHash = planHash,
            PolicyHash = "benchmark-policy",
            ArtifactHash = mode == ExecutionMode.Compiled ? ArtifactHash : null
        };

    private static async Task<ExecutionPlan> PreparePlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync("""
        {
          "id": "auto-hotness-bookkeeping-probe",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "functions": [{
            "id": "ShouldHandle",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "Bool",
            "body": [{ "op": "return", "value": { "bool": true } }]
          }]
        }
        """);
        var policy = SandboxPolicyBuilder.Create().WithFuel(100).Build();
        return await host.PrepareAsync(module, policy);
    }

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
