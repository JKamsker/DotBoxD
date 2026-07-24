using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I32WhileLoopPlanStoreTests
{
    [Fact]
    public void Distinct_statement_references_are_admitted_and_retrieved_independently()
    {
        var span = new SourceSpan(1, 1);
        var condition = new BinaryExpression(Variable("counter", span), "<", Variable("limit", span), span);
        var expression = new BinaryExpression(Variable("counter", span), "+", Literal(1, span), span);
        var assignment = new AssignmentStatement("counter", expression, span);
        var firstStatement = new WhileStatement(condition, [assignment], span);
        var secondStatement = firstStatement with { };
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                new AssignmentStatement("counter", Literal(0, span), span),
                new AssignmentStatement("limit", Literal(1, span), span),
                firstStatement,
                secondStatement
            ]);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var frame = InterpreterFrame.Create(layout, function, LocalFunctionArguments.FromArray([]));
        frame.WriteInt32("counter", 0);
        frame.WriteInt32("limit", 1);
        Assert.True(I32ComparisonPlan.TryCreate(condition, frame, "", out var conditionPlan));
        Assert.True(I32ExpressionPlan.TryCreate(expression, frame, "", out var expressionPlan));
        var firstPlan = CreatePlan(firstStatement, frame, conditionPlan, expressionPlan);
        var secondPlan = CreatePlan(secondStatement, frame, conditionPlan, expressionPlan);

        ref var plans = ref layout.LoopPlans;
        Assert.False(plans.ShouldCacheI32WhilePlan(firstStatement));
        Assert.False(plans.ShouldCacheI32WhilePlan(secondStatement));
        Assert.True(plans.ShouldCacheI32WhilePlan(firstStatement));
        plans.CacheI32WhilePlan(firstPlan);
        Assert.True(plans.ShouldCacheI32WhilePlan(secondStatement));
        plans.CacheI32WhilePlan(secondPlan);

        Assert.True(plans.TryGetI32WhilePlan(firstStatement, frame, out var firstCached));
        Assert.True(plans.TryGetI32WhilePlan(secondStatement, frame, out var secondCached));
        Assert.Same(firstPlan, firstCached);
        Assert.Same(secondPlan, secondCached);
    }

    private static I32WhileLoopPlan CreatePlan(
        WhileStatement statement,
        InterpreterFrame frame,
        I32ComparisonPlan condition,
        I32ExpressionPlan expression)
        => new(
            statement,
            condition,
            frame.GetSlot("counter"),
            expression,
            bodyFuel: 1 + expression.FuelCost);

    private static VariableExpression Variable(string name, SourceSpan span) => new(name, span);

    private static LiteralExpression Literal(int value, SourceSpan span)
        => new(SandboxValue.FromInt32(value), span);
}
