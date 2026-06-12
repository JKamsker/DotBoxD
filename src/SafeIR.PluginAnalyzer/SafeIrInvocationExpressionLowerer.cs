namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrInvocationExpressionLowerer
{
    private const int EqualsArgumentCount = 1;
    private const int EqualsValueArgumentIndex = 0;
    private const string EqualsMethodName = "Equals";
    private const string EqualsArgumentCountMessage =
        "Instance Equals calls must have exactly one argument.";
    private const string EqualsOperandTypeMessage =
        "Instance Equals calls require operands with the same supported type.";

    public static SafeIrExpressionModel Lower(
        InvocationExpressionSyntax invocation,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (invocation.Expression is MemberAccessExpressionSyntax member &&
            string.Equals(member.Name.Identifier.ValueText, EqualsMethodName, StringComparison.Ordinal))
        {
            return LowerEquals(invocation, member, lowerExpression);
        }

        throw new NotSupportedException($"Unsupported plugin invocation '{invocation}'.");
    }

    private static SafeIrExpressionModel LowerEquals(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != EqualsArgumentCount)
        {
            throw new NotSupportedException(EqualsArgumentCountMessage);
        }

        var receiver = lowerExpression(member.Expression);
        var value = lowerExpression(arguments[EqualsValueArgumentIndex].Expression);
        if (!string.Equals(receiver.Type, value.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException(EqualsOperandTypeMessage);
        }

        return new SafeIrExpressionModel(
            $"{SafeIrGenerationNames.Helpers.Eq}({receiver.Source}, {value.Source})",
            SafeIrGenerationNames.ManifestTypes.Bool,
            receiver.Allocates || value.Allocates);
    }
}
