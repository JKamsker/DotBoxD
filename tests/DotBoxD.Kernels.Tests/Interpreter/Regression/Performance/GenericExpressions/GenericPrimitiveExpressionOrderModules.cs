using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions;

internal static class GenericPrimitiveExpressionOrderModules
{
    private static readonly SourceSpan Span = new(1, 1);

    public static SandboxModule IneligibleFault(bool intrinsicFirst)
    {
        var intrinsic = new CallExpression("math.floor", [F64(1.75)], null, Span);
        var fault = new BinaryExpression(F64(double.MaxValue), "*", F64(2), Span);
        var arithmetic = intrinsicFirst
            ? new BinaryExpression(intrinsic, "+", fault, Span)
            : new BinaryExpression(fault, "+", intrinsic, Span);
        return Module(
            $"generic-ineligible-fault-{(intrinsicFirst ? "right" : "left")}",
            new BinaryExpression(arithmetic, "<", F64(0), Span));
    }

    public static SandboxModule DeepConversion(int depth, bool leftDeep)
    {
        Expression expression = new CallExpression(
            "numeric.toF64",
            [new LiteralExpression(SandboxValue.FromInt32(1), Span)],
            null,
            Span);
        for (var i = 0; i < depth; i++)
        {
            expression = leftDeep
                ? new BinaryExpression(expression, "+", F64(1), Span)
                : new BinaryExpression(F64(1), "+", expression, Span);
        }

        return Module(
            $"generic-ineligible-conversion-{(leftDeep ? "left" : "right")}-{depth}",
            new BinaryExpression(expression, "<", F64(depth + 2), Span));
    }

    private static SandboxModule Module(string id, Expression expression)
        => new(
            id,
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [new SandboxFunction(
                "main",
                true,
                [],
                SandboxType.Bool,
                [new ReturnStatement(expression, Span)])],
            new Dictionary<string, string>());

    private static LiteralExpression F64(double value)
        => new(SandboxValue.FromDouble(value), Span);
}
