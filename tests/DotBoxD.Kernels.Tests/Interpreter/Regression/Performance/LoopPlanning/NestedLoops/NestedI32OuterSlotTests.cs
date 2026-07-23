using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Interpreter.Internal.Loops;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.NestedLoops;

public sealed class NestedI32OuterSlotTests
{
    [Fact]
    public void Cache_lookup_only_defers_the_explicit_outer_slot()
    {
        var setup = CreateSetup();
        var frame = CreateFrame(setup, assignTotal: true, assignOuter: false);
        var unassignedTarget = CreateFrame(setup, assignTotal: false, assignOuter: false);
        var innerSlot = frame.GetSlot(setup.Inner.LocalName);
        var outerSlot = frame.GetSlot(setup.Outer.LocalName);
        ref var loopPlans = ref setup.Layout.LoopPlans;

        Assert.False(loopPlans.TryGetI32ForRangePlan(
            setup.Inner,
            frame,
            innerSlot,
            out _));
        Assert.True(loopPlans.TryGetI32ForRangePlan(
            setup.Inner,
            frame,
            innerSlot,
            outerSlot,
            out _));
        Assert.False(loopPlans.TryGetI32ForRangePlan(
            setup.Inner,
            unassignedTarget,
            innerSlot,
            outerSlot,
            out _));
    }

    [Fact]
    public void Composite_runner_writes_outer_slot_before_dependent_reads()
    {
        var setup = CreateSetup();
        var frame = CreateFrame(setup, assignTotal: true, assignOuter: false);
        using var context = CreateContext(CancellationToken.None);

        var handled = NestedI32ForLoopRunner.TryRun(
            setup.Outer,
            start: 0,
            end: 3,
            frame,
            context,
            new SandboxExecutionOptions());

        Assert.True(handled);
        Assert.Equal(6, frame.ReadInt32("total"));
        Assert.Equal(57, context.Budget.FuelUsed);
        Assert.Equal(6, context.Budget.LoopIterations);
    }

    [Fact]
    public void Outer_dependent_path_checks_cancellation_before_outer_write()
    {
        var setup = CreateSetup();
        var frame = CreateFrame(setup, assignTotal: true, assignOuter: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var context = CreateContext(cancellation.Token);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            NestedI32ForLoopRunner.TryRun(
                setup.Outer,
                start: 0,
                end: 3,
                frame,
                context,
                new SandboxExecutionOptions()));
        Assert.False(frame.IsSlotAssigned(frame.GetSlot(setup.Outer.LocalName)));
        Assert.Equal(0, context.Budget.FuelUsed);
        Assert.Equal(0, context.Budget.LoopIterations);
    }

    [Fact]
    public void Composite_runner_captures_outer_dependent_bound_before_body_mutation()
    {
        var setup = CreateOuterMutationSetup();
        var frame = CreateFrame(setup, assignTotal: false, assignOuter: false);
        using var context = CreateContext(CancellationToken.None);

        var handled = NestedI32ForLoopRunner.TryRun(
            setup.Outer,
            start: 0,
            end: 3,
            frame,
            context,
            new SandboxExecutionOptions());

        Assert.True(handled);
        Assert.Equal(4, frame.ReadInt32(setup.Outer.LocalName));
        Assert.Equal(51, context.Budget.FuelUsed);
        Assert.Equal(6, context.Budget.LoopIterations);
    }

    private static OuterSlotSetup CreateSetup()
    {
        var span = new SourceSpan(1, 1);
        var total = new VariableExpression("total", span);
        var outerIndex = new VariableExpression("outerIndex", span);
        var innerIndex = new VariableExpression("innerIndex", span);
        var expression = new BinaryExpression(
            new BinaryExpression(total, "+", outerIndex, span),
            "+",
            innerIndex,
            span);
        var inner = new ForRangeStatement(
            "innerIndex",
            Literal(0, span),
            outerIndex,
            [new AssignmentStatement("total", expression, span)],
            span);
        var outer = new ForRangeStatement(
            "outerIndex",
            Literal(0, span),
            Literal(3, span),
            [inner],
            span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [new AssignmentStatement("total", Literal(0, span), span), outer]);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var setup = new OuterSlotSetup(layout, function, outer, inner);
        var admissionFrame = CreateFrame(setup, assignTotal: true, assignOuter: true);

        Assert.True(I32ExpressionPlan.TryCreate(
            expression,
            admissionFrame,
            inner.LocalName,
            out var expressionPlan));
        ref var loopPlans = ref layout.LoopPlans;
        loopPlans.CacheI32ForRangePlan(new I32ForLoopPlan(
            inner,
            admissionFrame.GetSlot("total"),
            expressionPlan,
            fuelPerIteration: 5 + 1 + expressionPlan.FuelCost,
            expressionPlan.GetRequiredRawSlots()));
        return setup;
    }

    private static OuterSlotSetup CreateOuterMutationSetup()
    {
        var span = new SourceSpan(1, 1);
        var outerIndex = new VariableExpression("outerIndex", span);
        var expression = new BinaryExpression(outerIndex, "+", Literal(1, span), span);
        var inner = new ForRangeStatement(
            "innerIndex",
            Literal(0, span),
            outerIndex,
            [new AssignmentStatement("outerIndex", expression, span)],
            span);
        var outer = new ForRangeStatement(
            "outerIndex",
            Literal(0, span),
            Literal(3, span),
            [inner],
            span);
        var function = new SandboxFunction("main", true, [], SandboxType.I32, [outer]);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var setup = new OuterSlotSetup(layout, function, outer, inner);
        var admissionFrame = CreateFrame(setup, assignTotal: false, assignOuter: true);

        Assert.True(I32ExpressionPlan.TryCreate(
            expression,
            admissionFrame,
            inner.LocalName,
            out var expressionPlan));
        ref var loopPlans = ref layout.LoopPlans;
        loopPlans.CacheI32ForRangePlan(new I32ForLoopPlan(
            inner,
            admissionFrame.GetSlot(outer.LocalName),
            expressionPlan,
            fuelPerIteration: 5 + 1 + expressionPlan.FuelCost,
            expressionPlan.GetRequiredRawSlots()));
        return setup;
    }

    private static InterpreterFrame CreateFrame(
        OuterSlotSetup setup,
        bool assignTotal,
        bool assignOuter)
    {
        var frame = InterpreterFrame.Create(
            setup.Layout,
            setup.Function,
            LocalFunctionArguments.FromArray([]));
        if (assignTotal)
        {
            frame.WriteInt32("total", 0);
        }

        if (assignOuter)
        {
            frame.WriteInt32(setup.Outer.LocalName, 0);
        }

        return frame;
    }

    private static SandboxContext CreateContext(CancellationToken cancellationToken)
    {
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxLoopIterations(100)
            .Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            cancellationToken);
    }

    private static LiteralExpression Literal(int value, SourceSpan span)
        => new(SandboxValue.FromInt32(value), span);

    private sealed record OuterSlotSetup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        ForRangeStatement Outer,
        ForRangeStatement Inner);
}
