using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledExecutionEnvelopeProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 1_000_000;
    private const double PreviousBytesPerOperation = 512.1D;
    private const double MaximumPooledBytesPerOperation = 200D;
    private const double MaximumAlternatingBytesPerOperation = 200D;
    private const double MinimumFreshBytesPerOperation = 500D;
    private const double MaximumFreshBytesPerOperation = 520D;
    private const double ExpectedStateSavingsBytesPerOperation = 320D;
    private const double AllocationNoiseBytesPerOperation = 2D;
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
        var alternatePlan = await PrepareAsync(host, CompiledExecutionEnvelopeModules.PureSuccessAlternate, policy);
        var failurePlan = await PrepareAsync(host, CompiledExecutionEnvelopeModules.PureFailure, policy);
        var warmResult = await host.ExecuteAsync(
            successPlan,
            "main",
            SandboxValue.Unit,
            SuppressedOptions);
        var expected = CompiledExecutionInvariant.FromWarmResult(successPlan, warmResult);
        var alternateWarmResult = await host.ExecuteAsync(
            alternatePlan,
            "main",
            SandboxValue.Unit,
            SuppressedOptions);
        var alternateExpected = CompiledExecutionInvariant.FromWarmResult(alternatePlan, alternateWarmResult);

        using var cancellation = new CancellationTokenSource();
        _ = Measure(host, successPlan, expected, cancellation.Token, WarmupIterations);
        ForceGc();
        var providerLookup = Measure(host, successPlan, expected, cancellation.Token, Iterations);
        _ = Measure(host, successPlan, expected, CancellationToken.None, WarmupIterations);
        ForceGc();
        var optimized = Measure(host, successPlan, expected, CancellationToken.None, Iterations);
        _ = CompiledExecutionAlternationProbe.Measure(
            host,
            successPlan,
            expected,
            alternatePlan,
            alternateExpected,
            SuppressedOptions,
            WarmupIterations);
        ForceGc();
        var alternating = CompiledExecutionAlternationProbe.Measure(
            host,
            successPlan,
            expected,
            alternatePlan,
            alternateExpected,
            SuppressedOptions,
            Iterations);
        var optimizedBytesPerOperation = optimized.Bytes / (double)Iterations;
        var alternatingBytesPerOperation = alternating.Bytes / (double)Iterations;
        var providerBytesPerOperation = providerLookup.Bytes / (double)Iterations;
        var savedBytesPerOperation = providerBytesPerOperation - optimizedBytesPerOperation;
        if (optimizedBytesPerOperation > MaximumPooledBytesPerOperation)
        {
            throw new InvalidOperationException(
                $"expected warmed public compiled hits to allocate at most " +
                $"{MaximumPooledBytesPerOperation:N1} B/op, got {optimizedBytesPerOperation:N1} B/op");
        }

        if (alternatingBytesPerOperation > MaximumAlternatingBytesPerOperation)
        {
            throw new InvalidOperationException(
                $"expected alternating warmed public compiled hits to allocate at most " +
                $"{MaximumAlternatingBytesPerOperation:N1} B/op, got {alternatingBytesPerOperation:N1} B/op");
        }

        if (providerBytesPerOperation is < MinimumFreshBytesPerOperation or > MaximumFreshBytesPerOperation)
        {
            throw new InvalidOperationException(
                $"expected cancelable provider lookups to allocate " +
                $"{MinimumFreshBytesPerOperation:N1}-{MaximumFreshBytesPerOperation:N1} B/op, " +
                $"got {providerBytesPerOperation:N1} B/op");
        }

        if (Math.Abs(savedBytesPerOperation - ExpectedStateSavingsBytesPerOperation) >
            AllocationNoiseBytesPerOperation)
        {
            throw new InvalidOperationException(
                $"expected pooled state to save {ExpectedStateSavingsBytesPerOperation:N1} B/op, " +
                $"got {savedBytesPerOperation:N1} B/op");
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
            $"completed executable hit       {optimized.ElapsedMilliseconds,8:N1} {optimized.Bytes,14:N0} " +
            $"{optimizedBytesPerOperation,10:N1} {optimized.Checksum,10:N0}");
        Console.WriteLine(
            $"alternating completed plans    {alternating.ElapsedMilliseconds,8:N1} {alternating.Bytes,14:N0} " +
            $"{alternatingBytesPerOperation,10:N1} {alternating.Checksum,10:N0}");
        Console.WriteLine(
            $"cancelable provider + fresh    {providerLookup.ElapsedMilliseconds,8:N1} {providerLookup.Bytes,14:N0} " +
            $"{providerBytesPerOperation,10:N1} {providerLookup.Checksum,10:N0}");
        Console.WriteLine(
            $"pooled state = {PreviousBytesPerOperation:N1} B/op previous -> " +
            $"{optimizedBytesPerOperation:N1} B/op current " +
            $"({savedBytesPerOperation:N1} B/op below fresh control)");
        Console.WriteLine(
            "resources F/Max/L/A/H/R/W/NR/NW/Log/Elem/String = " +
            CompiledExecutionInvariant.FormatUsage(expected.ResourceUsage));
        Console.WriteLine($"artifact = {expected.ArtifactHash}");
        Console.WriteLine(
            "controls = cancelable-provider/fresh-state, audited-success, compiled-failure, verifier-fallback: pinned");
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
        CancellationToken cancellationToken,
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
                cancellationToken);
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
