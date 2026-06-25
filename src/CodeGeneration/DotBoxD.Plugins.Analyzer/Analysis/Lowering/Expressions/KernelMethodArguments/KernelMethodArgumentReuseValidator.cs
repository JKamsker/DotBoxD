using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class KernelMethodArgumentReuseValidator
{
    public static void Validate(
        IMethodSymbol method,
        ExpressionSyntax body,
        BoundKernelMethodCall call)
    {
        var usageCounts = ParameterUsageCounts(method, body);
        foreach (var argument in call.Arguments)
        {
            if (usageCounts.TryGetValue(argument.Parameter.Name, out var count) &&
                count > 1 &&
                argument.Expression is { } expression &&
                !IsRepeatableArgument(expression))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' parameter '{argument.Parameter.Name}' is used more than once; " +
                    "pass a repeatable value or store the expensive argument before calling the kernel method.");
            }
        }
    }

    private static Dictionary<string, int> ParameterUsageCounts(IMethodSymbol method, ExpressionSyntax body)
    {
        var parameterNames = new HashSet<string>(
            method.Parameters.Select(static parameter => parameter.Name),
            StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var identifier in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.ValueText;
            if (!parameterNames.Contains(name))
            {
                continue;
            }

            counts[name] = counts.TryGetValue(name, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static bool IsRepeatableArgument(ExpressionSyntax expression)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsRepeatableArgument(parenthesized.Expression),
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax member => IsRepeatableArgument(member.Expression),
            PrefixUnaryExpressionSyntax unary => IsRepeatableArgument(unary.Operand),
            BinaryExpressionSyntax binary => IsRepeatableArgument(binary.Left) && IsRepeatableArgument(binary.Right),
            _ when expression.IsKind(SyntaxKind.DefaultLiteralExpression) => true,
            _ => false
        };
}
