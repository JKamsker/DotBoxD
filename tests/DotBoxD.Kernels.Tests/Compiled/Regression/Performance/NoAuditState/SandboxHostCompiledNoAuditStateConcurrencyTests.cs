using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

public sealed class SandboxHostCompiledNoAuditStateConcurrencyTests
{
    private const int WorkerCount = 8;
    private const int IterationsPerWorker = 1_000;

    private static readonly SandboxExecutionOptions Options = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    [Fact]
    public async Task Concurrent_same_plan_runs_keep_results_and_resource_usage_isolated()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PrepareAsync(host);
        var warm = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt32(0), Options);
        AssertSuccessfulResult(warm, expectedValue: 1);
        var expectedUsage = warm.ResourceUsage;
        using var start = new ManualResetEventSlim();
        var workers = Enumerable.Range(0, WorkerCount)
            .Select(worker => Task.Run(() => RunWorker(host, plan, expectedUsage, worker, start)))
            .ToArray();

        start.Set();
        var checksums = await Task.WhenAll(workers);

        var expectedChecksum = Enumerable.Range(0, WorkerCount)
            .Sum(worker => checked((long)(worker + 1) * IterationsPerWorker));
        Assert.Equal(expectedChecksum, checksums.Sum());
    }

    [Fact]
    public async Task Busy_host_slot_falls_back_to_fresh_state_then_recovers()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PrepareAsync(host);
        var probeExecutable = ProbeExecutable(plan);
        var heldLease = host.TryAcquireCompiledNoAuditState(
            plan,
            "main",
            probeExecutable,
            Options,
            CancellationToken.None,
            suppliedState: null,
            useAsyncWorker: false);
        var heldState = Assert.IsType<CompiledNoAuditRunState>(heldLease.State);
        try
        {
            var busyResult = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt32(6), Options);
            AssertSuccessfulResult(busyResult, expectedValue: 7);
            Assert.Equal(0, heldState.Budget.FuelUsed);
        }
        finally
        {
            heldLease.Dispose();
        }

        var reusedResult = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt32(8), Options);
        AssertSuccessfulResult(reusedResult, expectedValue: 9);
        Assert.Equal(reusedResult.ResourceUsage, heldState.Budget.Snapshot());
        using var verification = host.TryAcquireCompiledNoAuditState(
            plan,
            "main",
            probeExecutable,
            Options,
            CancellationToken.None,
            suppliedState: null,
            useAsyncWorker: false);
        Assert.Same(heldState, verification.State);
    }

    [Fact]
    public async Task Disposal_racing_lazy_initialization_never_leaves_a_live_pool()
    {
        using var preparationHost = SandboxTestHost.Create();
        var plan = await PrepareAsync(preparationHost);
        var executable = ProbeExecutable(plan);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var host = SandboxTestHost.Create();
            CompiledNoAuditRunStatePool.Lease lease = default;
            using var start = new ManualResetEventSlim();
            var acquire = Task.Run(() =>
            {
                start.Wait();
                lease = host.TryAcquireCompiledNoAuditState(
                    plan,
                    "main",
                    executable,
                    Options,
                    CancellationToken.None,
                    suppliedState: null,
                    useAsyncWorker: false);
            });
            var dispose = Task.Run(() =>
            {
                start.Wait();
                host.Dispose();
            });

            start.Set();
            await Task.WhenAll(acquire, dispose);
            Assert.False(host.HasCompiledNoAuditRunStatePool);
            lease.Dispose();
            host.Dispose();
        }
    }

    private static long RunWorker(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxResourceUsage expectedUsage,
        int worker,
        ManualResetEventSlim start)
    {
        start.Wait();
        var input = SandboxValue.FromInt32(worker);
        var expectedValue = worker + 1;
        long checksum = 0;
        for (var i = 0; i < IterationsPerWorker; i++)
        {
            var pending = host.ExecuteAsync(plan, "main", input, Options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Concurrent compiled execution unexpectedly became asynchronous.");
            }

            var result = pending.Result;
            if (result.ResourceUsage != expectedUsage)
            {
                throw new InvalidOperationException("Concurrent compiled execution shared resource state.");
            }

            AssertSuccessfulResult(result, expectedValue);
            checksum += expectedValue;
        }

        return checksum;
    }

    private static void AssertSuccessfulResult(SandboxExecutionResult result, int expectedValue)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expectedValue, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.Empty(result.AuditEvents);
    }

    private static CompiledExecutable ProbeExecutable(ExecutionPlan plan)
        => new(
            CompiledArtifactTestFactory.DynamicMethod(
                plan,
                static (_, _) => SandboxValue.FromInt32(0)),
            "Miss");

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(ModuleJson);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(long.MaxValue)
                .WithWallTime(TimeSpan.FromMinutes(5))
                .Build());
    }

    private const string ModuleJson = """
    {
      "id": "compiled-no-audit-state-pool-concurrency",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "value", "type": "I32" }],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "op": "add", "left": { "var": "value" }, "right": { "i32": 1 } }
        }]
      }]
    }
    """;
}
