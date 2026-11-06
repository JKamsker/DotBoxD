using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDInterpolatedStringExpressionLowerer
{
    public static DotBoxDExpressionModel Lower(
        InterpolatedStringExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var parts = new List<DotBoxDExpressionModel>();
        var hasInterpolation = false;
        foreach (var content in expression.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    AddText(parts, text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                    hasInterpolation = true;
                    parts.Add(LowerInterpolation(interpolation, context, lowerExpression));
                    break;
                default:
                    return Unsupported(expression);
            }
        }

        if (parts.Count == 0)
        {
            return Text(string.Empty);
        }

        if (parts.Count == 1)
        {
            return hasInterpolation ? Concat(Text(string.Empty), parts[0]) : parts[0];
        }

        var current = parts[0];
        for (var i = 1; i < parts.Count; i++)
        {
            current = Concat(current, parts[i]);
        }

        return current;
    }

    private static void AddText(List<DotBoxDExpressionModel> parts, string value)
    {
        if (value.Length > 0)
        {
            parts.Add(Text(value));
        }
    }

    private static DotBoxDExpressionModel LowerInterpolation(
        InterpolationSyntax interpolation,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (interpolation.AlignmentClause is not null ||
            interpolation.FormatClause is not null)
        {
            throw new NotSupportedException("String interpolation alignment and format clauses are not supported.");
        }

        // The wire tag alone does not identify the CLR formatting: enums and DateOnly also ride as Int and
        // polymorphic handle keys as String, but C# formats them by name/date/ToString(). Only a hole whose C#
        // type really is string or int has invariant kernel formatting.
        var clrType = context.SemanticModel.GetTypeInfo(interpolation.Expression, context.CancellationToken).Type?.SpecialType;
        var expression = lowerExpression(interpolation.Expression);
        if (clrType == SpecialType.System_String &&
            string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.String, StringComparison.Ordinal))
        {
            return expression;
        }

        if (clrType == SpecialType.System_Int32 &&
            string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.Int, StringComparison.Ordinal))
        {
            return Int32ToString(expression);
        }

        throw new NotSupportedException(
            $"String interpolation hole '{interpolation.Expression}' must be a string or int expression; format other values explicitly or use a remote handler.");
    }

    private static DotBoxDExpressionModel Int32ToString(DotBoxDExpressionModel expression)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Int32ToStr}({expression.Source})",
            DotBoxDGenerationNames.ManifestTypes.String,
            true);

    private static DotBoxDExpressionModel Text(string value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.String,
            true);

    private static DotBoxDExpressionModel Concat(DotBoxDExpressionModel left, DotBoxDExpressionModel right)
        => new(
            $"{DotBoxDGenerationNames.Helpers.ConcatString}({left.Source}, {right.Source})",
            DotBoxDGenerationNames.ManifestTypes.String,
            true);

    private static DotBoxDExpressionModel Unsupported(InterpolatedStringExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
