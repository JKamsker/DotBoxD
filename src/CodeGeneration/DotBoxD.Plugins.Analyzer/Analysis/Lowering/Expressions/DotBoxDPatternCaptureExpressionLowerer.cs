using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDPatternCaptureExpressionLowerer
{
    public static DotBoxDExpressionModel? TryLower(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionLoweringContext, DotBoxDExpressionModel> lowerExpression)
    {
        if (binary.Kind() == SyntaxKind.IsExpression)
        {
            return DotBoxDPatternExpressionLowerer.LowerIsTypeExpression(
                binary,
                context,
                part => lowerExpression(part, context));
        }

        if (binary.Kind() == SyntaxKind.LogicalAndExpression &&
            TryLowerDeclarationAnd(binary, context, lowerExpression) is { } declarationAnd)
        {
            return declarationAnd;
        }

        if (binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression &&
            DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(binary))
        {
            throw new NotSupportedException($"Unsupported declaration-pattern composition '{binary}'.");
        }

        return null;
    }

    public static bool TryLowerIdentifier(
        string name,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (context.TryGetPatternCapture(name, out var capture))
        {
            lowered = capture.Key;
            return true;
        }

        lowered = null!;
        return false;
    }

    private static DotBoxDExpressionModel? TryLowerDeclarationAnd(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionLoweringContext, DotBoxDExpressionModel> lowerExpression)
    {
        if (!DotBoxDPatternExpressionLowerer.TryLowerDeclarationPattern(
                binary.Left,
                context,
                part => lowerExpression(part, context),
                out var left,
                out var captureName,
                out var capture))
        {
            return null;
        }

        if (DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(binary.Right))
        {
            throw new NotSupportedException($"Unsupported declaration-pattern composition '{binary}'.");
        }

        var rightContext = context.WithPatternCapture(captureName, capture);
        var right = lowerExpression(binary.Right, rightContext);
        RequireBool(left, DotBoxDGenerationNames.Operators.LogicalAnd);
        RequireBool(right, DotBoxDGenerationNames.Operators.LogicalAnd);
        return new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.And}({left.Source}, {right.Source})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            left.Allocates || right.Allocates);
    }

    private static void RequireBool(DotBoxDExpressionModel expression, string symbol)
    {
        if (!string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Operator '{symbol}' requires bool operands.");
        }
    }
}
