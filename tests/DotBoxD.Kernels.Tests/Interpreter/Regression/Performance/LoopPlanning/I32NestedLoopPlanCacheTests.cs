using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class I32NestedLoopPlanCacheTests
{
    private const int Modulus = 1_000_003;

    [Fact]
    public async Task Repeated_nested_loop_entry_reuses_its_raw_i32_plan()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I32NestedLoopPlanCacheModules.NestedLoop);
        var interpreter = new SandboxInterpreter();
        var options = Options();

        _ = Execute(interpreter, plan, options, Input(outer: 4, inner: 1));
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var result = Execute(interpreter, plan, options, Input(outer: 20_000, inner: 1));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(60_000, ((I32Value)result.Value!).Value);
        Assert.Equal(40_000, result.ResourceUsage.LoopIterations);
        Assert.Equal(380_008, result.ResourceUsage.FuelUsed);
        Assert.True(
            allocated < 10_000,
            $"Nested loop execution allocated {allocated:N0} B; rebuilding each plan costs about 1.12 MB.");
    }

    [Fact]
    public async Task Alternating_nested_loops_reuse_both_raw_i32_plans()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I32NestedLoopPlanCacheModules.AlternatingNestedLoops);
        var interpreter = new SandboxInterpreter();
        var options = Options();

        _ = Execute(interpreter, plan, options, Input(outer: 4, inner: 1));
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var result = Execute(interpreter, plan, options, Input(outer: 20_000, inner: 1));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(60_000, ((I32Value)result.Value!).Value);
        Assert.Equal(60_000, result.ResourceUsage.LoopIterations);
        Assert.True(
            allocated < 10_000,
            $"Alternating nested loops allocated {allocated:N0} B; both plans should be cached.");
    }

    [Fact]
    public void Cached_plan_does_not_bypass_read_before_assignment()
    {
        var span = new SourceSpan(1, 1);
        var expression = new BinaryExpression(
            new VariableExpression("total", span),
            "+",
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            span);
        var assignment = new AssignmentStatement("total", expression, span);
        var loop = new ForRangeStatement(
            "i",
            new LiteralExpression(SandboxValue.FromInt32(0), span),
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            [assignment],
            span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [new AssignmentStatement("total", new LiteralExpression(SandboxValue.FromInt32(0), span), span), loop]);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var assignedFrame = InterpreterFrame.Create(
            layout,
            function,
            LocalFunctionArguments.FromArray([]));
        assignedFrame.WriteInt32("total", 0);
        Assert.True(I32ExpressionPlan.TryCreate(expression, assignedFrame, loop.LocalName, out var expressionPlan));

        var loopPlan = new I32ForLoopPlan(
            loop,
            assignedFrame.GetSlot("total"),
            expressionPlan,
            fuelPerIteration: 5 + 1 + expressionPlan.FuelCost,
            expressionPlan.GetRequiredRawSlots());
        ref var loopPlans = ref layout.LoopPlans;
        Assert.False(loopPlans.ShouldCacheI32ForRangePlan(loop));
        Assert.True(loopPlans.ShouldCacheI32ForRangePlan(loop));
        loopPlans.CacheI32ForRangePlan(loopPlan);
        Assert.True(loopPlans.TryGetI32ForRangePlan(
            loop,
            assignedFrame,
            assignedFrame.GetSlot(loop.LocalName),
            out _));

        var unassignedFrame = InterpreterFrame.Create(
            layout,
            function,
            LocalFunctionArguments.FromArray([]));
        Assert.False(loopPlans.TryGetI32ForRangePlan(
            loop,
            unassignedFrame,
            unassignedFrame.GetSlot(loop.LocalName),
            out _));
    }

    [Fact]
    public async Task Debug_trace_uses_the_traced_path_after_cache_warmup()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I32NestedLoopPlanCacheModules.NestedLoop);
        var interpreter = new SandboxInterpreter();

        _ = Execute(interpreter, plan, Options(), Input(outer: 2, inner: 1));
        var traced = Execute(
            interpreter,
            plan,
            Options(debug: true),
            Input(outer: 2, inner: 1));

        Assert.True(traced.Succeeded, traced.Error?.SafeMessage);
        Assert.Contains(
            traced.AuditEvents,
            audit => audit.Message?.Contains(
                "node=statement:ForRangeStatement",
                StringComparison.Ordinal) == true);
        Assert.Equal(
            3,
            traced.AuditEvents.Count(
                audit => audit.Message?.Contains(
                    "node=statement:AssignmentStatement",
                    StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task Concurrent_first_executions_publish_a_valid_plan()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I32NestedLoopPlanCacheModules.NestedLoop);
        var interpreter = new SandboxInterpreter();
        using var start = new ManualResetEventSlim();
        var executions = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return Execute(interpreter, plan, Options(), Input(outer: 100, inner: 1));
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(executions);

        Assert.All(results, result =>
        {
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(300, ((I32Value)result.Value!).Value);
        });
    }

    private static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxValue input)
    {
        var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new Xunit.Sdk.XunitException("nested-loop execution unexpectedly became asynchronous");
        }

        return pending.Result;
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(long.MaxValue)
                .WithMaxLoopIterations(long.MaxValue)
                .WithMaxAllocatedBytes(long.MaxValue)
                .WithMaxTotalCollectionElements(long.MaxValue)
                .Build());
    }

    private static SandboxExecutionOptions Options(bool debug = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = debug
        };

    private static SandboxValue Input(int outer, int inner)
        => SandboxValue.FromList(
            [SandboxValue.FromInt32(outer), SandboxValue.FromInt32(inner)],
            SandboxType.I32);

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
