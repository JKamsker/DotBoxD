using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class F64ForLoopPlanCancellationTests
{
    [Fact]
    public void Cached_plan_checks_precancellation_before_loop_mutation()
    {
        var setup = CreateSetup();
        setup.Layout.LoopPlans.CacheF64ForRangePlan(setup.Plan);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var context = CreateContext(cancellation.Token);

        Assert.ThrowsAny<OperationCanceledException>(() => F64ForLoopRunner.TryRun(
            setup.Statement,
            start: 0,
            end: 1,
            setup.Frame,
            context,
            new SandboxExecutionOptions()));

        Assert.Equal(0, context.Budget.FuelUsed);
        Assert.Equal(0, context.Budget.LoopIterations);
        Assert.False(setup.Frame.IsSlotAssigned(setup.Frame.GetSlot(setup.Statement.LocalName)));
        Assert.True(setup.Frame.TryReadDouble("total", out var total));
        Assert.Equal(1, total);
    }

    private static Setup CreateSetup()
    {
        var span = new SourceSpan(1, 1);
        var expression = new BinaryExpression(
            new VariableExpression("total", span),
            "+",
            new LiteralExpression(SandboxValue.FromDouble(1), span),
            span);
        var assignment = new AssignmentStatement("total", expression, span);
        var statement = new ForRangeStatement(
            "i",
            new LiteralExpression(SandboxValue.FromInt32(0), span),
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            [assignment],
            span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.F64,
            [
                new AssignmentStatement(
                    "total",
                    new LiteralExpression(SandboxValue.FromDouble(1), span),
                    span),
                statement
            ]);
        var bindings = new BindingRegistryBuilder().Build();
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            bindings);
        var frame = InterpreterFrame.Create(layout, function, LocalFunctionArguments.FromArray([]));
        frame.WriteRawDoubleSlot(frame.GetSlot("total"), 1);
        Assert.True(F64ExpressionPlan.TryCreate(
            expression,
            frame,
            "total",
            bindings,
            out var expressionPlan,
            out _));
        var plan = new F64ForLoopPlan(
            statement,
            frame.GetSlot("total"),
            expressionPlan,
            5 + 1 + expressionPlan.FuelCost);
        return new Setup(layout, statement, plan, frame);
    }

    private static SandboxContext CreateContext(CancellationToken cancellationToken)
    {
        var policy = SandboxPolicyBuilder.Create().Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            cancellationToken);
    }

    private sealed record Setup(
        FunctionFrameLayout Layout,
        ForRangeStatement Statement,
        F64ForLoopPlan Plan,
        InterpreterFrame Frame);
}
