using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class F64ForLoopPlanEligibilityTests
{
    [Fact]
    public void Reusable_classification_accepts_only_literals_and_raw_arithmetic()
    {
        var setup = CreateSetup();

        Assert.True(CreatePlan(setup.RawExpression, setup.Frame, setup.Bindings).IsReusableForLoopPlan);
        Assert.True(CreatePlan(setup.LiteralExpression, setup.Frame, setup.Bindings).IsReusableForLoopPlan);
        Assert.False(CreatePlan(setup.BoxedExpression, setup.Frame, setup.Bindings).IsReusableForLoopPlan);
        Assert.False(CreatePlan(setup.SqrtExpression, setup.Frame, setup.Bindings).IsReusableForLoopPlan);
    }

    [Fact]
    public void Reusable_plan_constructor_rejects_intrinsic_and_boxed_sources()
    {
        var setup = CreateSetup();
        var statement = Loop(setup.RawExpression.Span);

        Assert.Throws<ArgumentException>(() => new F64ForLoopPlan(
            statement,
            setup.Frame.GetSlot("result"),
            CreatePlan(setup.BoxedExpression, setup.Frame, setup.Bindings),
            10));
        Assert.Throws<ArgumentException>(() => new F64ForLoopPlan(
            statement,
            setup.Frame.GetSlot("result"),
            CreatePlan(setup.SqrtExpression, setup.Frame, setup.Bindings),
            10));
    }

    [Theory]
    [InlineData("math.sqrt")]
    [InlineData("math.floor")]
    [InlineData("math.ceil")]
    [InlineData("math.round")]
    public void Intrinsic_plans_are_never_reusable(string bindingId)
    {
        var setup = CreateSetup();
        var span = new SourceSpan(1, 1);
        var expression = new CallExpression(
            bindingId,
            [new VariableExpression("raw", span)],
            SandboxType.F64,
            span);

        var plan = CreatePlan(expression, setup.Frame, setup.Bindings);

        Assert.False(plan.IsReusableForLoopPlan);
        Assert.Equal(1, plan.BindingCallCount);
    }

    private static Setup CreateSetup()
    {
        var span = new SourceSpan(1, 1);
        var raw = new VariableExpression("raw", span);
        var boxed = new VariableExpression("boxed", span);
        var rawExpression = new BinaryExpression(
            raw,
            "+",
            new LiteralExpression(SandboxValue.FromDouble(1), span),
            span);
        var sqrtExpression = new CallExpression("math.sqrt", [raw], SandboxType.F64, span);
        var literalExpression = new LiteralExpression(SandboxValue.FromDouble(2), span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.F64,
            [
                Assign("raw", SandboxValue.FromDouble(4), span),
                Assign("boxed", SandboxValue.FromDouble(4), span),
                Assign("boxed", SandboxValue.FromString("different type"), span),
                Assign("result", SandboxValue.FromDouble(0), span)
            ]);
        var bindings = new BindingRegistryBuilder().AddDefaultPureBindings().Build();
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            bindings);
        var frame = InterpreterFrame.Create(layout, function, LocalFunctionArguments.FromArray([]));
        frame.WriteRawDoubleSlot(frame.GetSlot("raw"), 4);
        frame.Write("boxed", SandboxValue.FromDouble(4));
        frame.WriteRawDoubleSlot(frame.GetSlot("result"), 0);
        return new Setup(
            frame,
            bindings,
            rawExpression,
            boxed,
            sqrtExpression,
            literalExpression);
    }

    private static F64ExpressionPlan CreatePlan(
        Expression expression,
        InterpreterFrame frame,
        IBindingCatalog bindings)
    {
        Assert.True(F64ExpressionPlan.TryCreate(
            expression,
            frame,
            "result",
            bindings,
            out var plan,
            out _));
        return plan;
    }

    private static ForRangeStatement Loop(SourceSpan span)
        => new(
            "i",
            new LiteralExpression(SandboxValue.FromInt32(0), span),
            new LiteralExpression(SandboxValue.FromInt32(1), span),
            [],
            span);

    private static AssignmentStatement Assign(
        string name,
        SandboxValue value,
        SourceSpan span)
        => new(name, new LiteralExpression(value, span), span);

    private sealed record Setup(
        InterpreterFrame Frame,
        IBindingCatalog Bindings,
        Expression RawExpression,
        Expression BoxedExpression,
        Expression SqrtExpression,
        Expression LiteralExpression);
}
