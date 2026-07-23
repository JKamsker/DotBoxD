using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I64ForLoopPlanStoreTests
{
    [Fact]
    public void Cached_plan_revalidates_sources_and_uses_reference_identity()
    {
        var setup = CreateSetup();
        ref var plans = ref setup.Layout.LoopPlans;

        Assert.False(plans.ShouldCacheI64ForRangePlan(setup.Statement));
        Assert.True(plans.ShouldCacheI64ForRangePlan(setup.Statement));
        plans.CacheI64ForRangePlan(setup.Plan);

        Assert.True(plans.TryGetI64ForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var cached));
        Assert.Same(setup.Plan, cached);

        var equalStatement = setup.Statement with { };
        Assert.Equal(setup.Statement, equalStatement);
        Assert.NotSame(setup.Statement, equalStatement);
        Assert.False(plans.TryGetI64ForRangePlan(
            equalStatement,
            setup.AssignedFrame,
            out _));

        var unassignedFrame = CreateFrame(setup.Layout, setup.Function, assignSource: false);
        Assert.False(plans.TryGetI64ForRangePlan(
            setup.Statement,
            unassignedFrame,
            out _));
    }

    [Fact]
    public async Task Concurrent_admission_publishes_one_valid_plan()
    {
        var setup = CreateSetup();
        using var start = new ManualResetEventSlim();
        var admissions = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                ref var plans = ref setup.Layout.LoopPlans;
                if (plans.ShouldCacheI64ForRangePlan(setup.Statement))
                {
                    plans.CacheI64ForRangePlan(setup.Plan);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(admissions);

        ref var publishedPlans = ref setup.Layout.LoopPlans;
        Assert.True(publishedPlans.TryGetI64ForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var published));
        Assert.Same(setup.Plan, published);
    }

    [Fact]
    public void Shared_cache_keeps_i32_and_i64_plans_typed_and_reachable()
    {
        var setup = CreateSetup();
        ref var plans = ref setup.Layout.LoopPlans;

        Assert.False(plans.ShouldCacheI32ForRangePlan(setup.I32Statement));
        Assert.True(plans.ShouldCacheI32ForRangePlan(setup.I32Statement));
        plans.CacheI32ForRangePlan(setup.I32Plan);
        Assert.False(plans.ShouldCacheI64ForRangePlan(setup.Statement));
        Assert.True(plans.ShouldCacheI64ForRangePlan(setup.Statement));
        plans.CacheI64ForRangePlan(setup.Plan);

        var i32LoopSlot = setup.AssignedFrame.GetSlot(setup.I32Statement.LocalName);
        Assert.True(plans.TryGetI32ForRangePlan(
            setup.I32Statement,
            setup.AssignedFrame,
            i32LoopSlot,
            out var i32Plan));
        Assert.Same(setup.I32Plan, i32Plan);
        Assert.True(plans.TryGetI64ForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var i64Plan));
        Assert.Same(setup.Plan, i64Plan);

        Assert.False(plans.TryGetI32ForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            setup.AssignedFrame.GetSlot(setup.Statement.LocalName),
            out _));
        Assert.False(plans.TryGetI64ForRangePlan(
            setup.I32Statement,
            setup.AssignedFrame,
            out _));
    }

    private static Setup CreateSetup()
    {
        var span = new SourceSpan(1, 1);
        var expression = new BinaryExpression(
            new VariableExpression("source", span),
            "+",
            new LiteralExpression(SandboxValue.FromInt64(1), span),
            span);
        var assignment = new AssignmentStatement("total", expression, span);
        var statement = new ForRangeStatement(
            "i",
            new LiteralExpression(SandboxValue.FromInt32(0), span),
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            [assignment],
            span);
        var i32Expression = new BinaryExpression(
            new VariableExpression("intTotal", span),
            "+",
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            span);
        var i32Statement = new ForRangeStatement(
            "j",
            new LiteralExpression(SandboxValue.FromInt32(0), span),
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            [new AssignmentStatement("intTotal", i32Expression, span)],
            span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I64,
            [
                new AssignmentStatement(
                    "source",
                    new LiteralExpression(SandboxValue.FromInt64(1), span),
                    span),
                new AssignmentStatement(
                    "total",
                    new LiteralExpression(SandboxValue.FromInt64(0), span),
                    span),
                new AssignmentStatement(
                    "intTotal",
                    new LiteralExpression(SandboxValue.FromInt32(0), span),
                    span),
                i32Statement,
                statement
            ]);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var frame = CreateFrame(layout, function, assignSource: true);
        Assert.True(I64ExpressionPlan.TryCreate(expression, frame, out var expressionPlan));
        Assert.True(I32ExpressionPlan.TryCreate(
            i32Expression,
            frame,
            i32Statement.LocalName,
            out var i32ExpressionPlan));
        var plan = new I64ForLoopPlan(
            statement,
            frame.GetSlot("total"),
            expressionPlan,
            fuelPerIteration: 5 + 1 + expressionPlan.FuelCost);
        var i32Plan = new I32ForLoopPlan(
            i32Statement,
            frame.GetSlot("intTotal"),
            i32ExpressionPlan,
            fuelPerIteration: 5 + 1 + i32ExpressionPlan.FuelCost,
            i32ExpressionPlan.GetRequiredRawSlots());
        return new Setup(layout, function, statement, plan, i32Statement, i32Plan, frame);
    }

    private static InterpreterFrame CreateFrame(
        FunctionFrameLayout layout,
        SandboxFunction function,
        bool assignSource)
    {
        var frame = InterpreterFrame.Create(
            layout,
            function,
            LocalFunctionArguments.FromArray([]));
        frame.Write("total", SandboxValue.FromInt64(0));
        frame.WriteInt32("intTotal", 0);
        if (assignSource)
        {
            frame.Write("source", SandboxValue.FromInt64(1));
        }

        return frame;
    }

    private sealed record Setup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        ForRangeStatement Statement,
        I64ForLoopPlan Plan,
        ForRangeStatement I32Statement,
        I32ForLoopPlan I32Plan,
        InterpreterFrame AssignedFrame);
}
