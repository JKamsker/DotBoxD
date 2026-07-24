using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Interpreter.Internal.Loops;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.NestedLoops;

public sealed class NestedI32ForLoopFallbackTests
{
    [Fact]
    public void Cold_and_non_leaf_inner_bounds_stay_on_the_general_path()
    {
        var cold = CreateSetup(cachePlan: false, useNonLeafBound: false, omitRequiredTarget: false);
        var nonLeaf = CreateSetup(cachePlan: true, useNonLeafBound: true, omitRequiredTarget: false);
        using var context = CreateContext(CancellationToken.None);

        Assert.False(TryRun(cold, context));
        Assert.False(TryRun(nonLeaf, context));
        Assert.Equal(0, context.Budget.FuelUsed);
        Assert.Equal(0, context.Budget.LoopIterations);
    }

    [Fact]
    public void Cached_plan_with_an_unassigned_required_slot_fails_closed()
    {
        var setup = CreateSetup(cachePlan: true, useNonLeafBound: false, omitRequiredTarget: true);
        using var context = CreateContext(CancellationToken.None);

        Assert.False(TryRun(setup, context));
        Assert.Equal(0, context.Budget.FuelUsed);
        Assert.Equal(0, context.Budget.LoopIterations);
    }

    [Fact]
    public void Cached_composite_path_checks_cancellation_before_outer_mutation()
    {
        var setup = CreateSetup(cachePlan: true, useNonLeafBound: false, omitRequiredTarget: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var context = CreateContext(cancellation.Token);

        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            _ = TryRun(setup, context);
        });
        Assert.Equal(0, context.Budget.FuelUsed);
        Assert.Equal(0, context.Budget.LoopIterations);
        Assert.False(setup.Frame.IsSlotAssigned(setup.Frame.GetSlot(setup.Outer.LocalName)));
    }

    private static bool TryRun(RunnerSetup setup, SandboxContext context)
        => NestedI32ForLoopRunner.TryRun(
            setup.Outer,
            start: 0,
            end: 1,
            setup.Frame,
            context,
            new SandboxExecutionOptions());

    private static RunnerSetup CreateSetup(
        bool cachePlan,
        bool useNonLeafBound,
        bool omitRequiredTarget)
    {
        var span = new SourceSpan(1, 1);
        var total = new VariableExpression("total", span);
        var limit = new VariableExpression("limit", span);
        var assignmentExpression = new BinaryExpression(
            total,
            "+",
            Literal(1, span),
            span);
        var assignment = new AssignmentStatement("total", assignmentExpression, span);
        Expression innerEnd = useNonLeafBound
            ? new BinaryExpression(limit, "+", Literal(0, span), span)
            : limit;
        var inner = new ForRangeStatement(
            "innerIndex",
            Literal(0, span),
            innerEnd,
            [assignment],
            span);
        var outer = new ForRangeStatement(
            "outerIndex",
            Literal(0, span),
            Literal(1, span),
            [inner],
            span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                new AssignmentStatement("total", Literal(0, span), span),
                new AssignmentStatement("limit", Literal(1, span), span),
                outer
            ]);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var assignedFrame = CreateFrame(layout, function, assignTarget: true);

        if (cachePlan)
        {
            CacheInnerPlan(layout, assignedFrame, inner, assignmentExpression);
        }

        var frame = omitRequiredTarget
            ? CreateFrame(layout, function, assignTarget: false)
            : assignedFrame;
        return new RunnerSetup(outer, frame);
    }

    private static InterpreterFrame CreateFrame(
        FunctionFrameLayout layout,
        SandboxFunction function,
        bool assignTarget)
    {
        var frame = InterpreterFrame.Create(
            layout,
            function,
            LocalFunctionArguments.FromArray([]));
        frame.WriteInt32("limit", 1);
        if (assignTarget)
        {
            frame.WriteInt32("total", 0);
        }

        return frame;
    }

    private static void CacheInnerPlan(
        FunctionFrameLayout layout,
        InterpreterFrame frame,
        ForRangeStatement inner,
        Expression assignmentExpression)
    {
        Assert.True(I32ExpressionPlan.TryCreate(
            assignmentExpression,
            frame,
            inner.LocalName,
            out var expressionPlan));
        ref var loopPlans = ref layout.LoopPlans;
        loopPlans.CacheI32ForRangePlan(new I32ForLoopPlan(
            inner,
            frame.GetSlot("total"),
            expressionPlan,
            fuelPerIteration: 5 + 1 + expressionPlan.FuelCost,
            expressionPlan.GetRequiredRawSlots()));
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

    private sealed record RunnerSetup(ForRangeStatement Outer, InterpreterFrame Frame);
}
