using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class F64ForLoopPlanCacheRuntimeTests
{
    [Fact]
    public async Task Cached_plan_matches_a_cold_interpreter_for_changed_input()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, F64ForLoopPlanCacheModules.Counter);
        var cachedInterpreter = new SandboxInterpreter();

        AssertSuccess(Execute(cachedInterpreter, plan, SandboxValue.FromInt32(1)), 4);
        AssertSuccess(Execute(cachedInterpreter, plan, SandboxValue.FromInt32(1)), 4);
        var cached = Execute(cachedInterpreter, plan, SandboxValue.FromInt32(5));
        var cold = Execute(new SandboxInterpreter(), plan, SandboxValue.FromInt32(5));

        AssertSuccess(cached, 16);
        Assert.Equal(cold.ResourceUsage, cached.ResourceUsage);
        Assert.Equal(Bits(cold), Bits(cached));
    }

    [Fact]
    public async Task Cached_plan_preserves_nonfinite_failure_and_resource_order()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, F64ForLoopPlanCacheModules.NonFiniteMultiply);
        var interpreter = new SandboxInterpreter();
        var failingInput = SandboxValue.FromDouble(double.MaxValue);
        var fresh = Execute(interpreter, plan, failingInput);

        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromDouble(2)), 4);
        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromDouble(2)), 4);
        var cached = Execute(interpreter, plan, failingInput);

        Assert.False(fresh.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, fresh.Error!.Code);
        Assert.Equal(fresh.Error, cached.Error);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
    }

    [Fact]
    public async Task Positive_sqrt_warmup_does_not_cache_a_stale_sign_proof()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, F64ForLoopPlanCacheModules.Sqrt);
        var interpreter = new SandboxInterpreter();

        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromDouble(9)), 3);
        AssertSuccess(Execute(interpreter, plan, SandboxValue.FromDouble(16)), 4);
        var warmNegative = Execute(interpreter, plan, SandboxValue.FromDouble(-1));
        var coldNegative = Execute(new SandboxInterpreter(), plan, SandboxValue.FromDouble(-1));

        Assert.False(warmNegative.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, warmNegative.Error!.Code);
        Assert.Equal(coldNegative.Error, warmNegative.Error);
        Assert.Equal(coldNegative.ResourceUsage, warmNegative.ResourceUsage);
        Assert.Equal(1, warmNegative.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Debug_trace_uses_the_generic_statement_path_after_cache_warmup()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            F64ForLoopPlanCacheModules.Counter,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .WithMaxAllocatedBytes(long.MaxValue)
                .Build());
        var interpreter = new SandboxInterpreter();
        var input = SandboxValue.FromInt32(1);

        AssertSuccess(Execute(interpreter, plan, input), 4);
        AssertSuccess(Execute(interpreter, plan, input), 4);
        var traced = Execute(interpreter, plan, input, debug: true);

        AssertSuccess(traced, 4);
        var traces = traced.AuditEvents.Where(audit => audit.Kind == "DebugTrace").ToArray();
        Assert.Equal(
            [
                "statement:AssignmentStatement",
                "expression:LiteralExpression",
                "statement:ForRangeStatement",
                "expression:LiteralExpression",
                "expression:VariableExpression",
                "statement:AssignmentStatement",
                "expression:BinaryExpression",
                "expression:VariableExpression",
                "expression:LiteralExpression",
                "statement:ReturnStatement",
                "expression:VariableExpression"
            ],
            traces.Select(Node));
        Assert.Equal(
            ["998", "997", "996", "995", "994", "988", "987", "986", "985", "984", "983"],
            traces.Select(audit => audit.Fields!["fuelRemaining"]));
    }

    [Fact]
    public async Task Concurrent_first_executions_keep_frames_and_results_isolated()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, F64ForLoopPlanCacheModules.Counter);
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
            AssertSuccess(result, 1 + (3 * iterations));
            Assert.Equal(iterations, result.ResourceUsage.LoopIterations);
        }
    }

    [Theory]
    [InlineData(20L, long.MaxValue)]
    [InlineData(long.MaxValue, 1L)]
    public async Task Cached_plan_preserves_low_quota_fallback(
        long maxFuel,
        long maxLoopIterations)
    {
        using var host = SandboxTestHost.Create();
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .WithMaxAllocatedBytes(long.MaxValue)
            .Build();
        var plan = await PrepareAsync(host, F64ForLoopPlanCacheModules.Counter, policy);
        var interpreter = new SandboxInterpreter();
        var input = SandboxValue.FromInt32(3);

        var fresh = Execute(interpreter, plan, input);
        _ = Execute(interpreter, plan, input);
        var cached = Execute(interpreter, plan, input);

        Assert.False(fresh.Succeeded);
        Assert.Equal(fresh.Error, cached.Error);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
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
        Assert.True(pending.IsCompletedSuccessfully, "F64 loop execution unexpectedly became asynchronous");
        return pending.Result;
    }

    private static void AssertSuccess(SandboxExecutionResult result, double expected)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), Bits(result));
    }

    private static long Bits(SandboxExecutionResult result)
        => BitConverter.DoubleToInt64Bits(((F64Value)result.Value!).Value);

    private static string Node(SandboxAuditEvent audit)
        => $"{audit.Fields!["category"]}:{audit.Fields["nodeKind"]}";
}
