using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions;

internal static class WideExpressionEligibilityCacheModules
{
    private static readonly SourceSpan Span = new(1, 1);

    public static (SandboxModule Prepared, SandboxModule Unassigned) ConditionalAssignment()
    {
        var comparison = Comparison(Arithmetic("value"), F64(3));
        return (
            ConditionalAssignmentModule(
                "wide-eligibility-assignment-prepared",
                comparison,
                assignInElse: true),
            ConditionalAssignmentModule(
                "wide-eligibility-assignment-unassigned",
                comparison,
                assignInElse: false));
    }

    public static SandboxModule ManyDistinctVariables(int variableCount)
    {
        if (variableCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(variableCount));
        }

        var body = new List<Statement>(variableCount + 1);
        for (var i = 0; i < variableCount; i++)
        {
            body.Add(new AssignmentStatement($"value{i}", F64(1), Span));
        }

        Expression sum = new VariableExpression("value0", Span);
        for (var i = 1; i < variableCount; i++)
        {
            sum = new BinaryExpression(
                sum,
                "+",
                new VariableExpression($"value{i}", Span),
                Span);
        }

        body.Add(new ReturnStatement(Comparison(sum, F64(variableCount + 1)), Span));
        return Module(
            $"wide-eligibility-distinct-{variableCount}",
            [new SandboxFunction("main", true, [], SandboxType.Bool, body)]);
    }

    public static SandboxModule SaturatedCache(int overflowDepth)
    {
        var functions = new List<SandboxFunction>(5);
        for (var i = 0; i < 4; i++)
        {
            functions.Add(new SandboxFunction(
                $"seed{i}",
                true,
                [],
                SandboxType.Bool,
                [new ReturnStatement(
                    Comparison(
                        new BinaryExpression(F64(i + 1), "+", F64(1), Span),
                        F64(i + 3)),
                    Span)]));
        }

        Expression overflow = new CallExpression("math.floor", [F64(1.75)], null, Span);
        for (var i = 0; i < overflowDepth; i++)
        {
            overflow = new BinaryExpression(overflow, "+", F64(1), Span);
        }

        functions.Add(new SandboxFunction(
            "overflow",
            true,
            [],
            SandboxType.Bool,
            [new ReturnStatement(Comparison(overflow, F64(overflowDepth + 2)), Span)]));
        return Module($"wide-eligibility-saturated-{overflowDepth}", functions);
    }

    public static SandboxModule TwoArithmeticOperands(int depth)
    {
        var left = NumericTree(depth, "left");
        var right = NumericTree(depth, "right");
        return Module(
            $"wide-eligibility-two-operands-{depth}",
            [new SandboxFunction(
                "main",
                true,
                [],
                SandboxType.Bool,
                [
                    new AssignmentStatement("left", F64(1), Span),
                    new AssignmentStatement("right", F64(2), Span),
                    new ReturnStatement(Comparison(left, right), Span)
                ])]);
    }

    public static SandboxModule SharedUnaryAcrossLayouts()
    {
        var shared = new UnaryExpression("-", new VariableExpression("value", Span), Span);
        return Module(
            "wide-eligibility-shared-unary-layouts",
            [
                UnaryFunction("i64", shared, I64(1), I64(0)),
                UnaryFunction("f64", shared, F64(1), F64(0))
            ]);
    }

    public static long ExpectedDistinctFuel(int variableCount) => checked((4L * variableCount) + 3);

    public static long ExpectedOverflowFuel(int depth) => checked((2L * depth) + 8);

    private static SandboxFunction UnaryFunction(
        string id,
        UnaryExpression shared,
        LiteralExpression value,
        LiteralExpression limit)
        => new(
            id,
            true,
            [],
            SandboxType.Bool,
            [
                new AssignmentStatement("value", value, Span),
                new ReturnStatement(Comparison(shared, limit), Span)
            ]);

    private static Expression NumericTree(int depth, string variable)
    {
        Expression expression = new VariableExpression(variable, Span);
        for (var i = 0; i < depth; i++)
        {
            expression = new BinaryExpression(
                expression,
                "+",
                new VariableExpression(variable, Span),
                Span);
        }

        return expression;
    }

    private static BinaryExpression Arithmetic(string variable)
        => new(new VariableExpression(variable, Span), "+", F64(1), Span);

    private static SandboxModule ConditionalAssignmentModule(
        string id,
        BinaryExpression comparison,
        bool assignInElse)
        => Module(
            id,
            [new SandboxFunction(
                "main",
                true,
                [new Parameter("assign", SandboxType.Bool)],
                SandboxType.Bool,
                [
                    new IfStatement(
                        new VariableExpression("assign", Span),
                        [new AssignmentStatement("value", F64(1), Span)],
                        assignInElse
                            ? [new AssignmentStatement("value", F64(2), Span)]
                            : [],
                        Span),
                    new ReturnStatement(comparison, Span)
                ])]);

    private static BinaryExpression Comparison(Expression left, Expression right)
        => new(left, "<", right, Span);

    private static SandboxModule Module(string id, IReadOnlyList<SandboxFunction> functions)
        => new(
            id,
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            functions,
            new Dictionary<string, string>());

    private static LiteralExpression F64(double value)
        => new(SandboxValue.FromDouble(value), Span);

    private static LiteralExpression I64(long value)
        => new(SandboxValue.FromInt64(value), Span);
}
