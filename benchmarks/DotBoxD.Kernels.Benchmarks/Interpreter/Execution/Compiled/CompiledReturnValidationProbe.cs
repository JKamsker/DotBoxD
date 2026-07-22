using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledReturnValidationProbe
{
    private const int MaximumWarmupIterations = 2_000;
    private static readonly SandboxExecutionOptions Options = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    public static async Task RunAsync()
    {
        using var host = CreateHost();
        var policy = CreatePolicy();
        var scenarios = CompiledReturnValidationScenarios.Create();
        var prepared = new List<PreparedReturnValidationScenario>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            prepared.Add(await PrepareAsync(host, policy, scenario));
        }

        Console.WriteLine("compiled return-validation probe");
        Console.WriteLine(
            "case                    ops   compiled ms      ns/op    allocated B     B/op   walk ns/op  walk B/op   checksum");
        foreach (var scenario in prepared)
        {
            var validation = MeasureValidation(scenario.Scenario);
            var compiled = MeasureCompiled(host, scenario);
            Write(scenario.Scenario.Name, compiled, validation);
        }

        Console.WriteLine("accounting F/Max/L/A/H/R/W/NR/NW/Log/Elem/String");
        foreach (var scenario in prepared)
        {
            Console.WriteLine($"{scenario.Scenario.Name,-22} {Format(scenario.ExpectedUsage)}");
        }
    }

    private static async Task<PreparedReturnValidationScenario> PrepareAsync(
        SandboxHost host,
        SandboxPolicy policy,
        CompiledReturnValidationScenario scenario)
    {
        var module = await host.ImportJsonAsync(scenario.ModuleJson);
        var plan = await host.PrepareAsync(module, policy);
        PreparedReturnValidationScenario? prepared = null;
        var warmupIterations = Math.Min(MaximumWarmupIterations, scenario.CompiledIterations / 5);
        for (var i = 0; i < warmupIterations; i++)
        {
            var result = await host.ExecuteAsync(plan, "main", scenario.Input, Options);
            prepared ??= PreparedReturnValidationScenario.FromWarmResult(scenario, plan, result);
            prepared.Validate(result);
        }

        return prepared ?? throw new InvalidOperationException("compiled return-validation warmup did not run");
    }

    private static ProbeMeasurement MeasureCompiled(
        SandboxHost host,
        PreparedReturnValidationScenario scenario)
    {
        ForceGc();

        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < scenario.Scenario.CompiledIterations; i++)
        {
            var pending = host.ExecuteAsync(scenario.Plan, "main", scenario.Scenario.Input, Options);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("compiled return-validation probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            scenario.Validate(result);
            checksum += scenario.Scenario.ChecksumContribution;
        }

        watch.Stop();
        return ProbeMeasurement.Create(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            scenario.Scenario.CompiledIterations);
    }

    private static ProbeMeasurement MeasureValidation(CompiledReturnValidationScenario scenario)
    {
        for (var i = 0; i < 5_000; i++)
        {
            SandboxValueValidator.RequireType(scenario.Input, scenario.ReturnType, "probe return type mismatch");
        }

        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < scenario.ValidationIterations; i++)
        {
            SandboxValueValidator.RequireType(scenario.Input, scenario.ReturnType, "probe return type mismatch");
            checksum += scenario.ChecksumContribution;
        }

        watch.Stop();
        return ProbeMeasurement.Create(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            scenario.ValidationIterations);
    }

    private static SandboxHost CreateHost()
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxPolicy CreatePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private static void Write(
        string name,
        ProbeMeasurement compiled,
        ProbeMeasurement validation)
        => Console.WriteLine(
            $"{name,-22} {compiled.Iterations,6:N0} {compiled.Milliseconds,13:N1} " +
            $"{compiled.NanosecondsPerOperation,10:N1} {compiled.AllocatedBytes,14:N0} " +
            $"{compiled.BytesPerOperation,8:N1} {validation.NanosecondsPerOperation,12:N1} " +
            $"{validation.BytesPerOperation,10:N1} {compiled.Checksum,10:N0}");

    private static string Format(ResourceUsageInvariant usage)
        => $"{usage.FuelUsed}/{usage.MaxFuel}/{usage.LoopIterations}/{usage.AllocatedBytes}/" +
           $"{usage.HostCalls}/{usage.FileBytesRead}/{usage.FileBytesWritten}/" +
           $"{usage.NetworkBytesRead}/{usage.NetworkBytesWritten}/{usage.LogEvents}/" +
           $"{usage.CollectionElements}/{usage.StringBytes}";

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

internal sealed record PreparedReturnValidationScenario(
    CompiledReturnValidationScenario Scenario,
    ExecutionPlan Plan,
    string ArtifactHash,
    ResourceUsageInvariant ExpectedUsage)
{
    public static PreparedReturnValidationScenario FromWarmResult(
        CompiledReturnValidationScenario scenario,
        ExecutionPlan plan,
        SandboxExecutionResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ArtifactHash))
        {
            throw new InvalidOperationException("compiled return-validation warmup has no artifact identity");
        }

        var prepared = new PreparedReturnValidationScenario(
            scenario,
            plan,
            result.ArtifactHash,
            ResourceUsageInvariant.From(result.ResourceUsage));
        prepared.Validate(result);
        return prepared;
    }

    public void Validate(SandboxExecutionResult result)
    {
        if (result is not { Succeeded: true, Error: null } ||
            !ReferenceEquals(result.Value, Scenario.Input) ||
            result.ActualMode != ExecutionMode.Compiled ||
            !result.ExecutionDispatched ||
            result.AuditEvents.Count != 0 ||
            !StringComparer.Ordinal.Equals(result.ModuleHash, Plan.ModuleHash) ||
            !StringComparer.Ordinal.Equals(result.PlanHash, Plan.PlanHash) ||
            !StringComparer.Ordinal.Equals(result.PolicyHash, Plan.PolicyHash) ||
            !StringComparer.Ordinal.Equals(result.ArtifactHash, ArtifactHash) ||
            ResourceUsageInvariant.From(result.ResourceUsage) != ExpectedUsage)
        {
            throw new InvalidOperationException(
                $"compiled return-validation invariant changed for '{Scenario.Name}': " +
                $"success={result.Succeeded}, error={result.Error?.Code}, " +
                $"sameValue={ReferenceEquals(result.Value, Scenario.Input)}, mode={result.ActualMode}, " +
                $"dispatched={result.ExecutionDispatched}, audit={result.AuditEvents.Count}, " +
                $"module={StringComparer.Ordinal.Equals(result.ModuleHash, Plan.ModuleHash)}, " +
                $"plan={StringComparer.Ordinal.Equals(result.PlanHash, Plan.PlanHash)}, " +
                $"policy={StringComparer.Ordinal.Equals(result.PolicyHash, Plan.PolicyHash)}, " +
                $"artifact={StringComparer.Ordinal.Equals(result.ArtifactHash, ArtifactHash)}, " +
                $"usage={ResourceUsageInvariant.From(result.ResourceUsage) == ExpectedUsage}");
        }
    }
}

internal readonly record struct ProbeMeasurement(
    double Milliseconds,
    long AllocatedBytes,
    long Checksum,
    int Iterations)
{
    public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

    public double BytesPerOperation => AllocatedBytes / (double)Iterations;

    public static ProbeMeasurement Create(double milliseconds, long bytes, long checksum, int iterations)
    {
        if (checksum == 0)
        {
            throw new InvalidOperationException("compiled return-validation checksum was not consumed");
        }

        return new ProbeMeasurement(milliseconds, bytes, checksum, iterations);
    }
}
