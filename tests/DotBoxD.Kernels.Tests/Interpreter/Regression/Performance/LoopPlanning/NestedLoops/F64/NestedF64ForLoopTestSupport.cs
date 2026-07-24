using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.NestedLoops.F64;

internal static class NestedF64ForLoopTestSupport
{
    public static RunnerSetup CreateSetup(
        BoundKind boundKind = BoundKind.StableVariable,
        bool cachePlan = true,
        bool assignTarget = true,
        bool assignLoopSlots = false)
    {
        var span = new SourceSpan(1, 1);
        var total = new VariableExpression("total", span);
        var expression = new BinaryExpression(
            total,
            "+",
            new LiteralExpression(SandboxValue.FromDouble(1.5), span),
            span);
        var assignment = new AssignmentStatement("total", expression, span);
        var inner = new ForRangeStatement(
            "innerIndex",
            InnerStart(boundKind, span),
            InnerEnd(boundKind, span),
            [assignment],
            span);
        var outer = new ForRangeStatement(
            "outerIndex",
            I32(0, span),
            I32(1, span),
            [inner],
            span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.F64,
            [
                new AssignmentStatement("total", F64(0, span), span),
                new AssignmentStatement("limit", I32(1, span), span),
                outer
            ]);
        var bindings = new BindingRegistryBuilder().Build();
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            bindings);
        var admissionFrame = CreateFrame(
            layout,
            function,
            assignTarget: true,
            assignLoopSlots: true);
        if (cachePlan)
        {
            CachePlan(
                layout,
                admissionFrame,
                inner,
                expression,
                bindings);
        }

        var frame = CreateFrame(
            layout,
            function,
            assignTarget,
            assignLoopSlots);
        return new RunnerSetup(layout, function, outer, inner, frame);
    }

    public static SandboxContext CreateContext(
        long maxFuel = 1_000,
        long maxLoopIterations = 100,
        CancellationToken cancellationToken = default)
    {
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            cancellationToken);
    }

    private static InterpreterFrame CreateFrame(
        FunctionFrameLayout layout,
        SandboxFunction function,
        bool assignTarget,
        bool assignLoopSlots)
    {
        var frame = InterpreterFrame.Create(
            layout,
            function,
            LocalFunctionArguments.FromArray([]));
        frame.WriteInt32("limit", 1);
        if (assignTarget)
        {
            frame.WriteRawDoubleSlot(frame.GetSlot("total"), 0);
        }

        if (assignLoopSlots)
        {
            frame.WriteInt32("outerIndex", 0);
            frame.WriteInt32("innerIndex", 0);
        }

        return frame;
    }

    private static void CachePlan(
        FunctionFrameLayout layout,
        InterpreterFrame frame,
        ForRangeStatement inner,
        Expression expression,
        BindingRegistry bindings)
    {
        Assert.True(F64ExpressionPlan.TryCreate(
            expression,
            frame,
            "total",
            bindings,
            out var expressionPlan,
            out var binding));
        Assert.Null(binding);
        layout.LoopPlans.CacheF64ForRangePlan(new F64ForLoopPlan(
            inner,
            frame.GetSlot("total"),
            expressionPlan,
            5 + 1 + expressionPlan.FuelCost));
    }

    private static Expression InnerStart(BoundKind kind, SourceSpan span)
        => kind == BoundKind.OverflowingLiterals
            ? I32(int.MinValue, span)
            : I32(0, span);

    private static Expression InnerEnd(BoundKind kind, SourceSpan span)
        => kind switch
        {
            BoundKind.StableVariable => new VariableExpression("limit", span),
            BoundKind.Arithmetic => new BinaryExpression(
                new VariableExpression("limit", span),
                "+",
                I32(0, span),
                span),
            BoundKind.OuterLoopSlot => new VariableExpression("outerIndex", span),
            BoundKind.InnerLoopSlot => new VariableExpression("innerIndex", span),
            BoundKind.OverflowingLiterals => I32(int.MaxValue, span),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static LiteralExpression I32(int value, SourceSpan span)
        => new(SandboxValue.FromInt32(value), span);

    private static LiteralExpression F64(double value, SourceSpan span)
        => new(SandboxValue.FromDouble(value), span);

    internal enum BoundKind
    {
        StableVariable,
        Arithmetic,
        OuterLoopSlot,
        InnerLoopSlot,
        OverflowingLiterals
    }

    internal sealed record RunnerSetup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        ForRangeStatement Outer,
        ForRangeStatement Inner,
        InterpreterFrame Frame);
}
