using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I64ForLoopPlanCacheRuntimeTests
{
    [Fact]
    public async Task Cached_plan_preserves_checked_failure_and_resource_order()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I64ForLoopPlanCacheModules.CheckedAddition);
        var cachedInterpreter = new SandboxInterpreter();

        var fresh = Execute(cachedInterpreter, plan, SandboxValue.FromInt64(long.MaxValue));
        AssertSuccess(Execute(cachedInterpreter, plan, SandboxValue.FromInt64(1)), 2);
        AssertSuccess(Execute(cachedInterpreter, plan, SandboxValue.FromInt64(1)), 2);
        var cached = Execute(cachedInterpreter, plan, SandboxValue.FromInt64(long.MaxValue));

        Assert.Equal(fresh.Error, cached.Error);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
        Assert.False(cached.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, cached.Error!.Code);
        Assert.Equal("integer overflow", cached.Error.SafeMessage);
    }

    [Fact]
    public async Task Debug_trace_uses_the_generic_statement_path_after_cache_warmup()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I64ForLoopPlanCacheModules.Counter);
        var interpreter = new SandboxInterpreter();

        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromInt32(1)), 1);
        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromInt32(1)), 1);
        var traced = Execute(
            interpreter,
            plan,
            SandboxValue.FromInt32(1),
            debug: true);

        AssertSuccess(traced, 1);
        Assert.Contains(traced.AuditEvents, audit => IsTrace(audit, "ForRangeStatement"));
        Assert.Equal(2, traced.AuditEvents.Count(audit => IsTrace(audit, "AssignmentStatement")));
    }

    [Fact]
    public async Task Concurrent_first_executions_return_isolated_results()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I64ForLoopPlanCacheModules.Counter);
        var interpreter = new SandboxInterpreter();
        using var start = new ManualResetEventSlim();
        var executions = Enumerable.Range(1, 8)
            .Select(iterations => Task.Run(() =>
            {
                start.Wait();
                return (iterations, Result: Execute(
                    interpreter,
                    plan,
                    SandboxValue.FromInt32(iterations)));
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(executions);

        foreach (var (iterations, result) in results)
        {
            AssertSuccess(result, iterations);
            Assert.Equal(iterations, result.ResourceUsage.LoopIterations);
        }
    }

    [Fact]
    public async Task Cached_multi_plan_preserves_source_order_without_preassigned_intermediate()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I64ForLoopPlanCacheModules.MultiSourceOrdered);
        var interpreter = new SandboxInterpreter();

        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromInt64(1)), 3);
        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromInt64(4)), 6);
        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromInt64(9)), 11);
    }

    [Fact]
    public async Task Cached_multi_plan_preserves_second_assignment_overflow_and_resources()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I64ForLoopPlanCacheModules.MultiCheckedSecond);
        var interpreter = new SandboxInterpreter();
        var input = SandboxValue.FromInt64(long.MaxValue - 1);

        var fresh = Execute(interpreter, plan, input);
        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromInt64(1)), 3);
        var cached = Execute(interpreter, plan, input);

        Assert.Equal(fresh.Error, cached.Error);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
        Assert.False(cached.Succeeded);
        Assert.Equal("integer overflow", cached.Error!.SafeMessage);
    }

    [Theory]
    [InlineData(20L, long.MaxValue)]
    [InlineData(long.MaxValue, 1L)]
    public async Task Cached_multi_plan_preserves_low_quota_fallback(
        long maxFuel,
        long maxLoopIterations)
    {
        using var host = SandboxTestHost.Create();
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .WithMaxAllocatedBytes(long.MaxValue)
            .Build();
        var plan = await PrepareAsync(host, I64ForLoopPlanCacheModules.MultiQuota, policy);
        var interpreter = new SandboxInterpreter();
        var input = SandboxValue.FromInt64(1);

        var fresh = Execute(interpreter, plan, input);
        _ = Execute(interpreter, plan, input);
        var cached = Execute(interpreter, plan, input);

        Assert.False(fresh.Succeeded);
        Assert.Equal(fresh.Error, cached.Error);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
    }

    [Fact]
    public async Task Debug_trace_uses_generic_multi_statement_path_after_cache_warmup()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I64ForLoopPlanCacheModules.MultiSourceOrdered);
        var interpreter = new SandboxInterpreter();

        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromInt64(1)), 3);
        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromInt64(1)), 3);
        var traced = Execute(interpreter, plan, SandboxValue.FromInt64(1), debug: true);

        AssertSuccess(traced, 3);
        Assert.Equal(3, traced.AuditEvents.Count(audit => IsTrace(audit, "AssignmentStatement")));
        Assert.Single(traced.AuditEvents, audit => IsTrace(audit, "ForRangeStatement"));
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson,
        SandboxPolicy? policy = null)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy ?? SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .Build());
    }

    private static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        bool debug = false)
    {
        var pending = interpreter.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                AllowFallbackToInterpreter = false,
                SuppressSuccessfulRunSummaryAudit = true,
                EnableDebugTrace = debug
            },
            CancellationToken.None);
        Assert.True(pending.IsCompletedSuccessfully, "I64 loop execution unexpectedly became asynchronous");
        return pending.Result;
    }

    private static void AssertSuccess(SandboxExecutionResult result, long expected)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((I64Value)result.Value!).Value);
    }

    private static bool IsTrace(SandboxAuditEvent audit, string node)
        => audit.Message?.Contains($"node=statement:{node}", StringComparison.Ordinal) == true;
}
