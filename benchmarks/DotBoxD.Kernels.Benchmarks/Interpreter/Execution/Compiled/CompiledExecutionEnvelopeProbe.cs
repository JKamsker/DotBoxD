using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledExecutionEnvelopeProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 50_000;
    private const double HistoricalBytesPerOperation = 808.1D;
    private const double MaximumExpectedBytesPerOperation = 520D;
    private static readonly SandboxExecutionOptions SuppressedOptions = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    public static async Task RunAsync()
    {
        using var host = CreateCompiledHost();
        var policy = CreatePolicy();
        var successPlan = await PrepareAsync(host, CompiledExecutionEnvelopeModules.PureSuccess, policy);
        var failurePlan = await PrepareAsync(host, CompiledExecutionEnvelopeModules.PureFailure, policy);
        var warmResult = await host.ExecuteAsync(
            successPlan,
            "main",
            SandboxValue.Unit,
            SuppressedOptions);
        var expected = CompiledExecutionInvariant.FromWarmResult(successPlan, warmResult);

        _ = Measure(host, successPlan, expected, WarmupIterations);
        ForceGc();
        var measurement = Measure(host, successPlan, expected, Iterations);
        var bytesPerOperation = measurement.Bytes / (double)Iterations;
        if (bytesPerOperation > MaximumExpectedBytesPerOperation)
        {
            throw new InvalidOperationException(
                $"expected warmed public compiled hits to allocate at most " +
                $"{MaximumExpectedBytesPerOperation:N1} B/op, got {bytesPerOperation:N1} B/op");
        }

        await CompiledExecutionEnvelopeControls.ValidateAsync(
            host,
            successPlan,
            failurePlan,
            expected,
            policy);

        Console.WriteLine($"compiled public execution-envelope executions = {Iterations:N0}");
        Console.WriteLine("case                         total ms    allocated B       B/op   checksum");
        Console.WriteLine(
            $"public compiled cache hit     {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes,14:N0} " +
            $"{bytesPerOperation,10:N1} {measurement.Checksum,10:N0}");
        Console.WriteLine(
            $"allocation-only = {HistoricalBytesPerOperation:N1} B/op baseline -> " +
            $"{bytesPerOperation:N1} B/op current");
        Console.WriteLine(
            "resources F/Max/L/A/H/R/W/NR/NW/Log/Elem/String = " +
            CompiledExecutionInvariant.FormatUsage(expected.ResourceUsage));
        Console.WriteLine($"artifact = {expected.ArtifactHash}");
        Console.WriteLine("controls = audited-success, compiled-failure, verifier-fallback: pinned");
    }

    private static SandboxHost CreateCompiledHost()
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxPolicy CreatePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy);
    }

    private static CompiledEnvelopeMeasurement Measure(
        SandboxHost host,
        ExecutionPlan plan,
        CompiledExecutionInvariant expected,
        int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var pending = host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                SuppressedOptions,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("compiled execution-envelope probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            expected.ValidateSuppressedSuccess(result);
            checksum += ((I32Value)result.Value!).Value;
        }

        watch.Stop();
        var expectedChecksum = checked(7L * iterations);
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException($"expected checksum {expectedChecksum}, got {checksum}");
        }

        return new CompiledEnvelopeMeasurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

internal readonly record struct CompiledEnvelopeMeasurement(
    double ElapsedMilliseconds,
    long Bytes,
    long Checksum);

internal sealed record CompiledExecutionInvariant(
    ExecutionPlan Plan,
    string ArtifactHash,
    ResourceUsageInvariant ResourceUsage)
{
    private static readonly ResourceUsageInvariant ExpectedResourceUsage = new(
        FuelUsed: 4,
        MaxFuel: long.MaxValue,
        LoopIterations: 0,
        AllocatedBytes: 0,
        HostCalls: 0,
        FileBytesRead: 0,
        FileBytesWritten: 0,
        NetworkBytesRead: 0,
        NetworkBytesWritten: 0,
        LogEvents: 0,
        CollectionElements: 0,
        StringBytes: 0);

    public static CompiledExecutionInvariant FromWarmResult(ExecutionPlan plan, SandboxExecutionResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ArtifactHash))
        {
            throw new InvalidOperationException("compiled warmup did not return an artifact hash");
        }

        var expected = new CompiledExecutionInvariant(plan, result.ArtifactHash, ExpectedResourceUsage);
        expected.ValidateSuppressedSuccess(result);
        return expected;
    }

    public void ValidateSuppressedSuccess(SandboxExecutionResult result)
    {
        ValidateCompiledEnvelope(result);
        if (result is not { Succeeded: true, Error: null, Value: I32Value { Value: 7 } } ||
            result.AuditEvents.Count != 0)
        {
            throw new InvalidOperationException("suppressed compiled result value or audit envelope changed");
        }
    }

    public void ValidateCompiledEnvelope(SandboxExecutionResult result)
    {
        if (result.ActualMode != ExecutionMode.Compiled ||
            !result.ExecutionDispatched ||
            !StringComparer.Ordinal.Equals(result.ModuleHash, Plan.ModuleHash) ||
            !StringComparer.Ordinal.Equals(result.PlanHash, Plan.PlanHash) ||
            !StringComparer.Ordinal.Equals(result.PolicyHash, Plan.PolicyHash) ||
            !StringComparer.Ordinal.Equals(result.ArtifactHash, ArtifactHash))
        {
            throw new InvalidOperationException("compiled execution identity or dispatch envelope changed");
        }

        var usage = ResourceUsageInvariant.From(result.ResourceUsage);
        if (usage != ResourceUsage)
        {
            throw new InvalidOperationException($"expected resource usage {ResourceUsage}, got {usage}");
        }
    }

    public static string FormatUsage(ResourceUsageInvariant usage)
        => $"{usage.FuelUsed}/{usage.MaxFuel}/{usage.LoopIterations}/{usage.AllocatedBytes}/" +
           $"{usage.HostCalls}/{usage.FileBytesRead}/{usage.FileBytesWritten}/" +
           $"{usage.NetworkBytesRead}/{usage.NetworkBytesWritten}/{usage.LogEvents}/" +
           $"{usage.CollectionElements}/{usage.StringBytes}";
}
