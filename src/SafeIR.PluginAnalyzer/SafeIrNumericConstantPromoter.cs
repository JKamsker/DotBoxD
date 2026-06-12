namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrNumericConstantPromoter
{
    public static void Promote(
        BinaryExpressionSyntax binary,
        SafeIrExpressionLoweringContext context,
        ref SafeIrExpressionModel left,
        ref SafeIrExpressionModel right)
    {
        if (string.Equals(left.Type, right.Type, StringComparison.Ordinal) ||
            !SafeIrNumericExpressionLowerer.IsNumeric(left) ||
            !SafeIrNumericExpressionLowerer.IsNumeric(right)) {
            return;
        }

        if (TryPromote(binary.Left, context, right.Type) is { } promotedLeft) {
            left = promotedLeft;
            return;
        }

        if (TryPromote(binary.Right, context, left.Type) is { } promotedRight) {
            right = promotedRight;
        }
    }

    private static SafeIrExpressionModel? TryPromote(
        ExpressionSyntax expression,
        SafeIrExpressionLoweringContext context,
        string targetType)
    {
        try
        {
            return SafeIrConstantExpressionLowerer.TryLower(
                expression,
                context.SemanticModel,
                context.CancellationToken,
                targetType);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
