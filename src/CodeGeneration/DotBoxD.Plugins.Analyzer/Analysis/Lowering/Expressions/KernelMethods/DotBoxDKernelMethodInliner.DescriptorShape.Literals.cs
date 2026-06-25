using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static DescriptorShape PrimitiveLiteralShape(
        string name,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (arguments.Count != 1 ||
            arguments[0].NameColon is not null ||
            !arguments[0].RefKindKeyword.IsKind(SyntaxKind.None))
        {
            throw new NotSupportedException("Generated descriptor contains stale literal metadata.");
        }

        var expression = arguments[0].Expression;
        return name switch
        {
            "Str" when LiteralValue(expression) is string =>
                DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.String, allocates: true),
            "I32" when TryInt32Literal(expression) =>
                DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Int),
            "I64" when TryInt64Literal(expression) =>
                DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Long),
            "F64" when TryFiniteDoubleLiteral(expression) =>
                DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Double),
            "Bool" when LiteralValue(expression) is bool =>
                DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Bool),
            _ => throw new NotSupportedException("Generated descriptor contains stale literal metadata.")
        };
    }

    private static bool TryInt32Literal(ExpressionSyntax expression)
        => TryInt32Value(expression, out _);

    private static bool TryInt32Value(ExpressionSyntax expression, out int value)
    {
        var negative = false;
        if (expression is PrefixUnaryExpressionSyntax prefix &&
            prefix.OperatorToken.IsKind(SyntaxKind.MinusToken))
        {
            negative = true;
            expression = prefix.Operand;
        }

        if (expression is not LiteralExpressionSyntax literal)
        {
            value = 0;
            return false;
        }

        switch (literal.Token.Value)
        {
            case int number:
                value = negative ? -number : number;
                return true;
            case uint number when negative && number == 2147483648U:
                value = int.MinValue;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryInt64Literal(ExpressionSyntax expression)
        => TryInt64LiteralValue(expression, out _);

    private static bool TryInt64LiteralValue(ExpressionSyntax expression, out long value)
    {
        var negative = false;
        if (expression is PrefixUnaryExpressionSyntax prefix &&
            prefix.OperatorToken.IsKind(SyntaxKind.MinusToken))
        {
            negative = true;
            expression = prefix.Operand;
        }

        if (expression is not LiteralExpressionSyntax literal)
        {
            value = 0;
            return false;
        }

        switch (literal.Token.Value)
        {
            case int number:
                value = negative ? -number : number;
                return true;
            case long number when !negative || number != long.MinValue:
                value = negative ? -number : number;
                return true;
            case ulong number when negative && number == 9223372036854775808UL:
                value = long.MinValue;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryFiniteDoubleLiteral(ExpressionSyntax expression)
    {
        if (!TryDoubleValue(expression, out var value))
        {
            return false;
        }

        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static object? LiteralValue(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal ? literal.Token.Value : null;

    private static bool TryDoubleValue(ExpressionSyntax expression, out double value)
    {
        var negative = false;
        if (expression is PrefixUnaryExpressionSyntax prefix &&
            prefix.OperatorToken.IsKind(SyntaxKind.MinusToken))
        {
            negative = true;
            expression = prefix.Operand;
        }

        if (expression is not LiteralExpressionSyntax literal)
        {
            value = 0;
            return false;
        }

        switch (literal.Token.Value)
        {
            case int number:
                value = number;
                break;
            case long number:
                value = number;
                break;
            case float number:
                value = number;
                break;
            case double number:
                value = number;
                break;
            default:
                value = 0;
                return false;
        }

        if (negative)
        {
            value = -value;
        }

        return true;
    }
}
