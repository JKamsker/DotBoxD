using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class F64ForLoopPlanStoreTests
{
    [Fact]
    public void Cached_plan_revalidates_sources_and_uses_reference_identity()
    {
        var setup = CreateSetup();
        ref var plans = ref setup.Layout.LoopPlans;

        Assert.False(plans.ShouldCacheF64ForRangePlan(setup.Statement));
        Assert.True(plans.ShouldCacheF64ForRangePlan(setup.Statement));
        plans.CacheF64ForRangePlan(setup.Plan);

        Assert.True(plans.TryGetF64ForRangePlan(setup.Statement, setup.AssignedFrame, out var cached));
        Assert.Same(setup.Plan, cached);

        var equalStatement = setup.Statement with { };
        Assert.Equal(setup.Statement, equalStatement);
        Assert.NotSame(setup.Statement, equalStatement);
        Assert.False(plans.TryGetF64ForRangePlan(equalStatement, setup.AssignedFrame, out _));

        var unassigned = CreateFrame(setup.Layout, setup.Function, assignSource: false);
        Assert.False(plans.TryGetF64ForRangePlan(setup.Statement, unassigned, out _));
        Assert.False(plans.TryGetI64ForRangePlan(setup.Statement, setup.AssignedFrame, out _));
    }

    [Fact]
    public void Literal_plan_has_no_invocation_assignment_requirement()
    {
        var setup = CreateSetup();
        ref var plans = ref setup.Layout.LoopPlans;
        plans.CacheF64ForRangePlan(setup.LiteralPlan);
        var unassigned = CreateFrame(setup.Layout, setup.Function, assignSource: false);

        Assert.True(plans.TryGetF64ForRangePlan(
            setup.LiteralStatement,
            unassigned,
            out var cached));
        Assert.Same(setup.LiteralPlan, cached);
    }

    [Fact]
    public void Shared_cache_keeps_multiple_f64_plans_typed_and_reachable()
    {
        var setup = CreateSetup();
        ref var plans = ref setup.Layout.LoopPlans;
        plans.CacheF64ForRangePlan(setup.Plan);
        plans.CacheF64ForRangePlan(setup.LiteralPlan);

        Assert.True(plans.TryGetF64ForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var first));
        Assert.True(plans.TryGetF64ForRangePlan(
            setup.LiteralStatement,
            setup.AssignedFrame,
            out var second));
        Assert.Same(setup.Plan, first);
        Assert.Same(setup.LiteralPlan, second);
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
                if (plans.ShouldCacheF64ForRangePlan(setup.Statement))
                {
                    plans.CacheF64ForRangePlan(setup.Plan);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(admissions);

        Assert.True(setup.Layout.LoopPlans.TryGetF64ForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var published));
        Assert.Same(setup.Plan, published);
    }

    private static Setup CreateSetup()
    {
        var span = new SourceSpan(1, 1);
        var expression = new BinaryExpression(
            new VariableExpression("source", span),
            "+",
            new LiteralExpression(SandboxValue.FromDouble(1), span),
            span);
        var statement = Loop("i", new AssignmentStatement("total", expression, span), span);
        var literalExpression = new LiteralExpression(SandboxValue.FromDouble(2), span);
        var literalStatement = Loop(
            "j",
            new AssignmentStatement("literalTotal", literalExpression, span),
            span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.F64,
            [
                Assign("source", 1, span),
                Assign("total", 0, span),
                Assign("literalTotal", 0, span),
                statement,
                literalStatement
            ]);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var frame = CreateFrame(layout, function, assignSource: true);
        Assert.True(F64ExpressionPlan.TryCreate(
            expression,
            frame,
            "total",
            new BindingRegistryBuilder().Build(),
            out var expressionPlan,
            out _));
        Assert.True(F64ExpressionPlan.TryCreate(
            literalExpression,
            frame,
            "literalTotal",
            new BindingRegistryBuilder().Build(),
            out var literalPlan,
            out _));
        return new Setup(
            layout,
            function,
            statement,
            Plan(statement, frame.GetSlot("total"), expressionPlan),
            literalStatement,
            Plan(literalStatement, frame.GetSlot("literalTotal"), literalPlan),
            frame);
    }

    private static F64ForLoopPlan Plan(
        ForRangeStatement statement,
        int targetSlot,
        F64ExpressionPlan expression)
        => new(statement, targetSlot, expression, 5 + 1 + expression.FuelCost);

    private static ForRangeStatement Loop(
        string local,
        AssignmentStatement assignment,
        SourceSpan span)
        => new(
            local,
            new LiteralExpression(SandboxValue.FromInt32(0), span),
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            [assignment],
            span);

    private static AssignmentStatement Assign(string name, double value, SourceSpan span)
        => new(name, new LiteralExpression(SandboxValue.FromDouble(value), span), span);

    private static InterpreterFrame CreateFrame(
        FunctionFrameLayout layout,
        SandboxFunction function,
        bool assignSource)
    {
        var frame = InterpreterFrame.Create(layout, function, LocalFunctionArguments.FromArray([]));
        frame.WriteRawDoubleSlot(frame.GetSlot("total"), 0);
        frame.WriteRawDoubleSlot(frame.GetSlot("literalTotal"), 0);
        if (assignSource)
        {
            frame.WriteRawDoubleSlot(frame.GetSlot("source"), 1);
        }

        return frame;
    }

    private sealed record Setup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        ForRangeStatement Statement,
        F64ForLoopPlan Plan,
        ForRangeStatement LiteralStatement,
        F64ForLoopPlan LiteralPlan,
        InterpreterFrame AssignedFrame);
}
