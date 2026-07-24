using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class GenericPrimitiveExpressionProbeModules
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
            $"probe-generic-f64-{(leftDeep ? "left" : "right")}-{depth}-" +
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

    public static SandboxModule I64Comparison(int depth, bool arithmeticOnLeft)
    {
        var expression = NumericTree(
            depth,
            leftDeep: true,
            static () => new VariableExpression("value", Span));
        return Module(
            $"probe-generic-i64-left-{depth}-" +
            $"{(arithmeticOnLeft ? "left-operand" : "right-operand")}",
            SandboxType.Bool,
            [
                new AssignmentStatement("value", I64(1), Span),
                new ReturnStatement(
                    arithmeticOnLeft
                        ? new BinaryExpression(expression, "<", I64(depth + 2), Span)
                        : new BinaryExpression(I64(0), "<", expression, Span),
                    Span)
            ]);
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
            $"probe-generic-f64-intrinsic-{(leftDeep ? "left" : "right")}-{depth}-" +
            $"{(arithmeticOnLeft ? "left-operand" : "right-operand")}",
            SandboxType.Bool,
            [new ReturnStatement(
                arithmeticOnLeft
                    ? new BinaryExpression(expression, "<", F64(depth + 2), Span)
                    : new BinaryExpression(F64(0), "<", expression, Span),
                Span)]);
    }

    public static SandboxModule F64ConversionComparison(int depth, bool leftDeep)
    {
        var conversion = new CallExpression("numeric.toF64", [I32(1)], null, Span);
        var expression = NumericTree(
            depth,
            leftDeep,
            static () => F64(1),
            conversion);
        return Module(
            $"probe-generic-f64-conversion-{(leftDeep ? "left" : "right")}-{depth}",
            SandboxType.Bool,
            [new ReturnStatement(
                new BinaryExpression(expression, "<", F64(depth + 2), Span),
                Span)]);
    }

    public static SandboxModule SaturatedCache(int overflowDepth)
    {
        var functions = new List<SandboxFunction>(5);
        for (var i = 0; i < 4; i++)
        {
            var conversion = new CallExpression("numeric.toF64", [I32(i + 1)], null, Span);
            functions.Add(new SandboxFunction(
                $"seed{i}",
                true,
                [],
                SandboxType.Bool,
                [new ReturnStatement(
                    new BinaryExpression(
                        new BinaryExpression(conversion, "+", F64(1), Span),
                        "<",
                        F64(i + 3),
                        Span),
                    Span)]));
        }

        Expression overflow = new CallExpression("math.floor", [F64(1.75)], null, Span);
        for (var i = 0; i < overflowDepth; i++)
        {
            overflow = new BinaryExpression(F64(1), "+", overflow, Span);
        }

        functions.Add(new SandboxFunction(
            "overflow",
            true,
            [],
            SandboxType.Bool,
            [new ReturnStatement(
                new BinaryExpression(F64(0), "<", overflow, Span),
                Span)]));
        return new SandboxModule(
            $"probe-wide-cache-saturated-{overflowDepth}",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            functions,
            new Dictionary<string, string>());
    }

    public static SandboxModule F64LiteralComparison(int depth)
        => Module(
            $"probe-generic-f64-literal-{depth}",
            SandboxType.Bool,
            [new ReturnStatement(
                new BinaryExpression(
                    NumericTree(depth, leftDeep: true, static () => F64(1)),
                    "<",
                    F64(depth + 2),
                    Span),
                Span)]);

    public static SandboxModule I32Tree(int depth, bool leftDeep)
        => Module(
            $"probe-generic-i32-{(leftDeep ? "left" : "right")}-{depth}",
            SandboxType.I32,
            [new ReturnStatement(NumericTree(depth, leftDeep, static () => I32(1)), Span)]);

    public static SandboxModule ShallowBool()
        => Module(
            "probe-generic-shallow-bool",
            SandboxType.Bool,
            [new ReturnStatement(new BinaryExpression(F64(1), "<", F64(2), Span), Span)]);

    public static SandboxModule ShallowIntrinsicCall()
        => Module(
            "probe-generic-shallow-intrinsic",
            SandboxType.F64,
            [new ReturnStatement(
                new CallExpression("math.floor", [F64(1.75)], null, Span),
                Span)]);

    public static SandboxModule EligibleLocalCall()
    {
        var helper = new SandboxFunction(
            "constant",
            false,
            [],
            SandboxType.I32,
            [new ReturnStatement(I32(7), Span)]);
        return Module(
            "probe-generic-eligible-local-call",
            SandboxType.I32,
            [new ReturnStatement(new CallExpression("constant", [], null, Span), Span)],
            helper);
    }

    public static SandboxModule F64Fault(int depth)
    {
        Expression expression = new BinaryExpression(F64(double.MaxValue), "*", F64(2), Span);
        for (var i = 1; i < depth; i++)
        {
            expression = new BinaryExpression(expression, "+", F64(1), Span);
        }

        return Module(
            $"probe-generic-f64-fault-{depth}",
            SandboxType.Bool,
            [new ReturnStatement(new BinaryExpression(expression, "<", F64(0), Span), Span)]);
    }

    public static long ExpectedComparisonFuel(int depth) => checked((2L * depth) + 7);

    public static long ExpectedI32Fuel(int depth) => checked((2L * depth) + 3);

    public static long ExpectedFaultFuel(int depth) => checked(depth + 5L);

    public static long ExpectedIntrinsicComparisonFuel(int depth) => checked((2L * depth) + 8);

    public static long ExpectedConversionComparisonFuel(int depth) => checked((2L * depth) + 6);

    public static long ExpectedLiteralComparisonFuel(int depth) => checked((2L * depth) + 5);

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
        params SandboxFunction[] helpers)
    {
        var functions = new SandboxFunction[helpers.Length + 1];
        functions[0] = new SandboxFunction("main", true, [], returnType, body);
        helpers.CopyTo(functions, 1);
        return new SandboxModule(
            id,
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            functions,
            new Dictionary<string, string>());
    }

    private static LiteralExpression I32(int value)
        => new(SandboxValue.FromInt32(value), Span);

    private static LiteralExpression I64(long value)
        => new(SandboxValue.FromInt64(value), Span);

    private static LiteralExpression F64(double value)
        => new(SandboxValue.FromDouble(value), Span);
}
