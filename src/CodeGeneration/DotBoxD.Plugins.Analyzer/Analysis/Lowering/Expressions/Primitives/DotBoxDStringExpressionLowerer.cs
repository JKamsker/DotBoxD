using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDStringExpressionLowerer
{
    private const string LengthPropertyName = "Length";

    public static DotBoxDExpressionModel? TryLowerMember(
        MemberAccessExpressionSyntax member,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        // Only string.Length belongs here; an array's Length is a list count handled by the member-chain lowerer.
        if (!string.Equals(member.Name.Identifier.ValueText, LengthPropertyName, StringComparison.Ordinal) ||
            context.SemanticModel.GetTypeInfo(member.Expression, context.CancellationToken).Type?.SpecialType !=
                SpecialType.System_String)
        {
            return null;
        }

        var receiver = lowerExpression(member.Expression);
        if (!string.Equals(receiver.Type, DotBoxDGenerationNames.ManifestTypes.String, StringComparison.Ordinal))
        {
            throw new NotSupportedException("String Length receiver must lower to string.");
        }

        return new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.StringLength}({receiver.Source})",
            DotBoxDGenerationNames.ManifestTypes.Int,
            receiver.Allocates);
    }
}
