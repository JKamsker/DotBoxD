using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.NestedLoops.F64;

public sealed class NestedF64ForLoopParityTests
{
    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, -1)]
    [InlineData(0, 2)]
    public async Task Cached_composite_path_reads_each_frames_fixed_bound(
        int outerIterations,
        int innerIterations)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            F64ForLoopPlanCacheModules.Nested,
            Policy());
        var optimizedInterpreter = new SandboxInterpreter();

        Warm(optimizedInterpreter, plan);
        var optimized = Execute(
            optimizedInterpreter,
            plan,
            Input(outerIterations, innerIterations));
        var generic = Execute(
            new SandboxInterpreter(),
            plan,
            Input(outerIterations, innerIterations));

        AssertEquivalent(generic, optimized);
        Assert.True(optimized.Succeeded, optimized.Error?.SafeMessage);
        var enteredInnerIterations = Math.Max(0, innerIterations);
        var expectedInnerExecutions = (long)outerIterations * enteredInnerIterations;
        var expected = 1.5 * expectedInnerExecutions;
        Assert.Equal(Bits(expected), Bits(optimized));
        Assert.Equal(
            outerIterations + expectedInnerExecutions,
            optimized.ResourceUsage.LoopIterations);
        Assert.Equal(
            8L + (8L * outerIterations) + (9L * expectedInnerExecutions),
            optimized.ResourceUsage.FuelUsed);
    }

    [Theory]
    [InlineData(31L, long.MaxValue, "fuel exhausted")]
    [InlineData(1_000L, 3L, "loop iteration budget exhausted")]
    public async Task Insufficient_aggregate_budget_preserves_generic_fault_order(
        long maxFuel,
        long maxLoopIterations,
        string expectedMessage)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            F64ForLoopPlanCacheModules.Nested,
            Policy(maxFuel, maxLoopIterations));
        var optimizedInterpreter = new SandboxInterpreter();

        Warm(optimizedInterpreter, plan);
        var optimized = Execute(optimizedInterpreter, plan, Input(2, 1));
        var generic = Execute(new SandboxInterpreter(), plan, Input(2, 1));

        AssertEquivalent(generic, optimized);
        Assert.False(optimized.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, optimized.Error!.Code);
        Assert.Equal(expectedMessage, optimized.Error.SafeMessage);
    }

    [Theory]
    [InlineData(41L, 4L, false)]
    [InlineData(42L, 4L, true)]
    public async Task Exact_loop_capacity_preserves_post_loop_fuel_boundary(
        long maxFuel,
        long maxLoopIterations,
        bool succeeds)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            F64ForLoopPlanCacheModules.Nested,
            Policy(maxFuel, maxLoopIterations));
        var optimizedInterpreter = new SandboxInterpreter();

        Warm(optimizedInterpreter, plan);
        var optimized = Execute(optimizedInterpreter, plan, Input(2, 1));
        var generic = Execute(new SandboxInterpreter(), plan, Input(2, 1));

        AssertEquivalent(generic, optimized);
        Assert.Equal(succeeds, optimized.Succeeded);
        Assert.Equal(42, optimized.ResourceUsage.FuelUsed);
        Assert.Equal(4, optimized.ResourceUsage.LoopIterations);
        if (succeeds)
        {
            Assert.Equal(Bits(3), Bits(optimized));
        }
        else
        {
            Assert.Equal(SandboxErrorCode.QuotaExceeded, optimized.Error!.Code);
            Assert.Equal("fuel exhausted", optimized.Error.SafeMessage);
        }
    }

    [Fact]
    public async Task Cached_composite_path_preserves_nonfinite_failure_order()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            NestedF64ForLoopModules.NonFinite,
            Policy());
        var optimizedInterpreter = new SandboxInterpreter();

        Assert.True(Execute(
            optimizedInterpreter,
            plan,
            SandboxValue.FromDouble(2)).Succeeded);
        Assert.True(Execute(
            optimizedInterpreter,
            plan,
            SandboxValue.FromDouble(2)).Succeeded);
        var input = SandboxValue.FromDouble(double.MaxValue);
        var optimized = Execute(optimizedInterpreter, plan, input);
        var generic = Execute(new SandboxInterpreter(), plan, input);

        AssertEquivalent(generic, optimized);
        Assert.False(optimized.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, optimized.Error!.Code);
    }

    [Fact]
    public async Task Debug_trace_retains_exact_generic_preorder_after_warmup()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            F64ForLoopPlanCacheModules.Nested,
            Policy(maxFuel: 1_000));
        var optimizedInterpreter = new SandboxInterpreter();
        Warm(optimizedInterpreter, plan);
        var input = Input(1, 1);

        var optimized = Execute(optimizedInterpreter, plan, input, debug: true);
        var generic = Execute(new SandboxInterpreter(), plan, input, debug: true);

        AssertEquivalent(generic, optimized);
        var expected = new[]
        {
            new TraceNode("statement", "AssignmentStatement", "998"),
            new TraceNode("expression", "LiteralExpression", "997"),
            new TraceNode("statement", "ForRangeStatement", "996"),
            new TraceNode("expression", "LiteralExpression", "995"),
            new TraceNode("expression", "VariableExpression", "994"),
            new TraceNode("statement", "ForRangeStatement", "988"),
            new TraceNode("expression", "LiteralExpression", "987"),
            new TraceNode("expression", "VariableExpression", "986"),
            new TraceNode("statement", "AssignmentStatement", "980"),
            new TraceNode("expression", "BinaryExpression", "979"),
            new TraceNode("expression", "VariableExpression", "978"),
            new TraceNode("expression", "LiteralExpression", "977"),
            new TraceNode("statement", "ReturnStatement", "976"),
            new TraceNode("expression", "VariableExpression", "975")
        };
        Assert.Equal(expected, Trace(optimized));
        Assert.Equal(Trace(generic), Trace(optimized));
    }

    [Fact]
    public async Task Warm_composite_plan_is_safe_across_concurrent_frames()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            F64ForLoopPlanCacheModules.Nested,
            Policy());
        var interpreter = new SandboxInterpreter();
        Warm(interpreter, plan);
        using var start = new ManualResetEventSlim();
        var executions = Enumerable.Range(1, 8)
            .Select(outer => Task.Run(() =>
            {
                start.Wait();
                return (outer, Result: Execute(interpreter, plan, Input(outer, 1)));
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(executions);

        foreach (var (outer, result) in results)
        {
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(Bits(1.5 * outer), Bits(result));
            Assert.Equal(2 * outer, result.ResourceUsage.LoopIterations);
            Assert.Equal(8 + (17L * outer), result.ResourceUsage.FuelUsed);
            Assert.Equal(2, result.ResourceUsage.CollectionElements);
            Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
            Assert.Equal(0, result.ResourceUsage.HostCalls);
        }
    }

    private static void Warm(SandboxInterpreter interpreter, ExecutionPlan plan)
    {
        Assert.True(Execute(interpreter, plan, Input(1, 1)).Succeeded);
        Assert.True(Execute(interpreter, plan, Input(1, 1)).Succeeded);
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
            Options(debug),
            CancellationToken.None);
        Assert.True(pending.IsCompletedSuccessfully);
        return pending.Result;
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy);
    }

    private static SandboxValue Input(int outer, int inner)
        => SandboxValue.FromList(
            [SandboxValue.FromInt32(outer), SandboxValue.FromInt32(inner)],
            SandboxType.I32);

    private static SandboxPolicy Policy(
        long maxFuel = long.MaxValue,
        long maxLoopIterations = long.MaxValue)
        => SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .Build();

    private static SandboxExecutionOptions Options(bool debug = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = debug
        };

    private static void AssertEquivalent(
        SandboxExecutionResult expected,
        SandboxExecutionResult actual)
    {
        Assert.Equal(expected.Succeeded, actual.Succeeded);
        Assert.Equal(expected.Error, actual.Error);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.ResourceUsage, actual.ResourceUsage);
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static long Bits(SandboxExecutionResult result)
        => Bits(((F64Value)result.Value!).Value);

    private static TraceNode[] Trace(SandboxExecutionResult result)
        => result.AuditEvents
            .Where(audit => audit.Kind == "DebugTrace")
            .Select(audit => new TraceNode(
                audit.Fields!["category"],
                audit.Fields["nodeKind"],
                audit.Fields["fuelRemaining"]))
            .ToArray();

    private sealed record TraceNode(string Category, string NodeKind, string FuelRemaining);
}
