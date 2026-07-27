using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions;

internal static class GenericPrimitiveExpressionModules
{
    private static readonly SourceSpan Span = new(1, 1);

    public static SandboxModule F64Comparison(
        int depth,
        bool leftDeep,
        bool arithmeticOnLeft = true)
    {
        var expression = NumericTree(
            depth,
            leftDeep,
            static () => new VariableExpression("value", Span));
        return Module(
            $"generic-f64-{(leftDeep ? "left" : "right")}-{depth}-" +
            $"{(arithmeticOnLeft ? "left-operand" : "right-operand")}",
            SandboxType.Bool,
            [
                new AssignmentStatement("value", F64(1), Span),
                new ReturnStatement(
                    arithmeticOnLeft
                        ? new BinaryExpression(expression, "<", F64(depth + 2), Span)
                        : new BinaryExpression(F64(0), "<", expression, Span),
                    Span)
            ]);
    }

    public static SandboxModule F64Fault(int depth)
    {
        Expression expression = new BinaryExpression(F64(double.MaxValue), "*", F64(2), Span);
        for (var i = 1; i < depth; i++)
        {
            expression = new BinaryExpression(expression, "+", F64(1), Span);
        }

        return Module(
            $"generic-f64-fault-{depth}",
            SandboxType.Bool,
            [new ReturnStatement(new BinaryExpression(expression, "<", F64(0), Span), Span)]);
    }

    public static SandboxModule F64IntrinsicComparison(
        int depth,
        bool leftDeep,
        bool arithmeticOnLeft)
    {
        var intrinsic = new CallExpression("math.floor", [F64(1.75)], null, Span);
        var expression = NumericTree(
            depth,
            leftDeep,
            static () => F64(1),
            intrinsic);
        return Module(
            $"generic-f64-intrinsic-{(leftDeep ? "left" : "right")}-{depth}-" +
            $"{(arithmeticOnLeft ? "left-operand" : "right-operand")}",
            SandboxType.Bool,
            [new ReturnStatement(
                arithmeticOnLeft
                    ? new BinaryExpression(expression, "<", F64(depth + 2), Span)
                    : new BinaryExpression(F64(0), "<", expression, Span),
                Span)]);
    }

    public static SandboxModule I64Comparison(int depth, bool leftDeep)
    {
        var expression = NumericTree(
            depth,
            leftDeep,
            static () => new VariableExpression("value", Span));
        return Module(
            $"generic-i64-{(leftDeep ? "left" : "right")}-{depth}",
            SandboxType.Bool,
            [
                new AssignmentStatement("value", I64(1), Span),
                new ReturnStatement(
                    new BinaryExpression(expression, "<", I64(depth + 2), Span),
                    Span)
            ]);
    }

    public static SandboxModule I64Fault()
        => Module(
            "generic-i64-fault",
            SandboxType.Bool,
            [new ReturnStatement(
                new BinaryExpression(
                    new BinaryExpression(I64(long.MaxValue), "+", I64(1), Span),
                    "<",
                    I64(0),
                    Span),
                Span)]);

    public static SandboxModule NestedIntrinsicCall()
        => Module(
            "generic-f64-nested-intrinsic",
            SandboxType.F64,
            [new ReturnStatement(
                new BinaryExpression(
                    new CallExpression("math.floor", [F64(3.75)], null, Span),
                    "+",
                    F64(1),
                    Span),
                Span)]);

    public static SandboxModule NestedNumericConversion()
        => Module(
            "generic-f64-nested-conversion",
            SandboxType.Bool,
            [new ReturnStatement(
                new BinaryExpression(
                    new BinaryExpression(
                        new CallExpression("numeric.toF64", [I32(3)], null, Span),
                        "+",
                        F64(1),
                        Span),
                    "<",
                    F64(5),
                    Span),
                Span)]);

    public static SandboxModule BoxedF64Comparison()
    {
        var comparison = new BinaryExpression(
            new BinaryExpression(new VariableExpression("value", Span), "+", F64(1), Span),
            "<",
            F64(3),
            Span);
        return Module(
            "generic-f64-boxed-source",
            SandboxType.Bool,
            [
                new AssignmentStatement("value", F64(1), Span),
                new IfStatement(
                    Bool(false),
                    [new AssignmentStatement(
                        "value",
                        new CallExpression(
                            "list.get",
                            [new VariableExpression("items", Span), I32(0)],
                            null,
                            Span),
                        Span)],
                    [],
                    Span),
                new ReturnStatement(comparison, Span)
            ],
            [new Parameter("items", SandboxType.List(SandboxType.F64))]);
    }

    public static SandboxModule UnknownF64Variable(bool arithmeticOnLeft)
    {
        var arithmetic = new BinaryExpression(
            new VariableExpression("missing", Span),
            "+",
            F64(1),
            Span);
        return Module(
            $"generic-f64-unknown-variable-{(arithmeticOnLeft ? "left" : "right")}",
            SandboxType.Bool,
            [new ReturnStatement(
                arithmeticOnLeft
                    ? new BinaryExpression(arithmetic, "<", F64(2), Span)
                    : new BinaryExpression(F64(2), "<", arithmetic, Span),
                Span)]);
    }

    public static long ExpectedComparisonFuel(int depth) => checked((2L * depth) + 7);

    public static long ExpectedFaultFuel(int depth) => checked(depth + 5L);

    public static long ExpectedIntrinsicComparisonFuel(int depth) => checked((2L * depth) + 8);

    private static Expression NumericTree(
        int depth,
        bool leftDeep,
        Func<Expression> leaf,
        Expression? deepestLeaf = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        Expression expression = deepestLeaf ?? leaf();
        for (var i = 0; i < depth; i++)
        {
            expression = leftDeep
                ? new BinaryExpression(expression, "+", leaf(), Span)
                : new BinaryExpression(leaf(), "+", expression, Span);
        }

        return expression;
    }

    private static SandboxModule Module(
        string id,
        SandboxType returnType,
        IReadOnlyList<Statement> body,
        IReadOnlyList<Parameter>? parameters = null)
        => new(
            id,
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [new SandboxFunction("main", true, parameters ?? [], returnType, body)],
            new Dictionary<string, string>());

    private static LiteralExpression F64(double value)
        => new(SandboxValue.FromDouble(value), Span);

    private static LiteralExpression I64(long value)
        => new(SandboxValue.FromInt64(value), Span);

    private static LiteralExpression I32(int value)
        => new(SandboxValue.FromInt32(value), Span);

    private static LiteralExpression Bool(bool value)
        => new(SandboxValue.FromBool(value), Span);
}
