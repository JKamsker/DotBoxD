using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class SandboxHostCompiledNoAuditStatePoolAllocationTests
{
    private const int WarmupIterations = 5_000;
    private const int MeasuredIterations = 100_000;
    private const double MaximumPooledBytesPerRun = 200;
    private const double MinimumFreshBytesPerRun = 500;
    private const double MaximumFreshBytesPerRun = 520;
    private const double ExpectedStateSavingsBytesPerRun = 320;
    private const double MeasurementNoiseBytesPerRun = 2;

    private static readonly SandboxExecutionOptions Options = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    [Fact]
    public async Task Sequential_public_compiled_runs_reuse_the_no_audit_meter_and_context()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PrepareAsync(host, ModuleJson);
        _ = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options);
        using var cancellation = new CancellationTokenSource();
        _ = Measure(host, plan, CancellationToken.None, WarmupIterations);
        _ = Measure(host, plan, cancellation.Token, WarmupIterations);
        ForceGc();

        var pooled = Measure(host, plan, CancellationToken.None, MeasuredIterations);
        var fresh = Measure(host, plan, cancellation.Token, MeasuredIterations);
        var pooledBytesPerRun = pooled.AllocatedBytes / (double)MeasuredIterations;
        var freshBytesPerRun = fresh.AllocatedBytes / (double)MeasuredIterations;
        var savedBytesPerRun = freshBytesPerRun - pooledBytesPerRun;

        Console.WriteLine(
            $"public compiled no-audit pool: pooled={pooledBytesPerRun:N3} B/run, " +
            $"cancelable={freshBytesPerRun:N3} B/run, saved={savedBytesPerRun:N3} B/run.");
        Assert.InRange(pooledBytesPerRun, 0, MaximumPooledBytesPerRun);
        Assert.InRange(freshBytesPerRun, MinimumFreshBytesPerRun, MaximumFreshBytesPerRun);
        Assert.InRange(
            savedBytesPerRun,
            ExpectedStateSavingsBytesPerRun - MeasurementNoiseBytesPerRun,
            ExpectedStateSavingsBytesPerRun + MeasurementNoiseBytesPerRun);
        Assert.Equal(checked(7L * MeasuredIterations), pooled.Checksum);
        Assert.Equal(pooled.Checksum, fresh.Checksum);
    }

    [Fact]
    public async Task Failed_run_releases_and_resets_the_state_for_following_successes()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PrepareAsync(host, FailureRecoveryModuleJson);
        var failed = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(0),
            Options);
        Assert.False(failed.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, failed.Error!.Code);
        Assert.Contains(failed.AuditEvents, audit =>
            audit is { Kind: "RunSummary", Success: false, ErrorCode: SandboxErrorCode.InvalidInput });

        var successful = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(2),
            Options);
        Assert.True(successful.Succeeded, successful.Error?.SafeMessage);
        Assert.Equal(5, Assert.IsType<I32Value>(successful.Value).Value);
        Assert.Empty(successful.AuditEvents);

        _ = MeasureRecovery(host, plan, successful.ResourceUsage, WarmupIterations);
        ForceGc();
        var measured = MeasureRecovery(host, plan, successful.ResourceUsage, MeasuredIterations);
        var bytesPerRun = measured.AllocatedBytes / (double)MeasuredIterations;
        Console.WriteLine($"public compiled failure recovery pool: {bytesPerRun:N3} B/run.");
        Assert.InRange(bytesPerRun, 0, MaximumPooledBytesPerRun);
        Assert.Equal(checked(5L * MeasuredIterations), measured.Checksum);
    }

    private static AllocationSummary Measure(
        SandboxHost host,
        ExecutionPlan plan,
        CancellationToken cancellationToken,
        int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0L;
        for (var i = 0; i < iterations; i++)
        {
            var pending = host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                Options,
                cancellationToken);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Compiled no-audit execution unexpectedly became asynchronous.");
            }

            var result = pending.Result;
            if (result is not
                {
                    Succeeded: true,
                    Error: null,
                    Value: I32Value { Value: 7 },
                    ActualMode: ExecutionMode.Compiled,
                    ExecutionDispatched: true,
                    AuditEvents.Count: 0
                } ||
                result.ResourceUsage.FuelUsed != 4)
            {
                throw new InvalidOperationException("Compiled no-audit execution changed its result or accounting.");
            }

            checksum += 7;
        }

        return new AllocationSummary(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static AllocationSummary MeasureRecovery(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxResourceUsage expectedUsage,
        int iterations)
    {
        var input = SandboxValue.FromInt32(2);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0L;
        for (var i = 0; i < iterations; i++)
        {
            var pending = host.ExecuteAsync(plan, "main", input, Options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Compiled recovery execution unexpectedly became asynchronous.");
            }

            var result = pending.Result;
            if (result is not
                {
                    Succeeded: true,
                    Error: null,
                    Value: I32Value { Value: 5 },
                    ActualMode: ExecutionMode.Compiled,
                    ExecutionDispatched: true,
                    AuditEvents.Count: 0
                } ||
                result.ResourceUsage != expectedUsage)
            {
                throw new InvalidOperationException("Compiled recovery changed its result or accounting.");
            }

            checksum += 5;
        }

        return new AllocationSummary(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host, string json)
    {
        var module = await host.ImportJsonAsync(json);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(long.MaxValue)
                .WithWallTime(TimeSpan.FromMinutes(5))
                .Build());
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private const string ModuleJson = """
    {
      "id": "compiled-no-audit-state-pool-allocation",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "i32": 7 } }]
      }]
    }
    """;

    private const string FailureRecoveryModuleJson = """
    {
      "id": "compiled-no-audit-state-pool-failure-recovery",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "divisor", "type": "I32" }],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "op": "div", "left": { "i32": 10 }, "right": { "var": "divisor" } }
        }]
      }]
    }
    """;

    private readonly record struct AllocationSummary(long AllocatedBytes, long Checksum);
}
