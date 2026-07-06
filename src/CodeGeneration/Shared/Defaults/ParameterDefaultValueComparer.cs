using Microsoft.CodeAnalysis;

namespace DotBoxD.CodeGeneration.Shared.Defaults;

internal static class ParameterDefaultValueComparer
{
    public static bool HasSameContract(
        IParameterSymbol left,
        IParameterSymbol right,
        DefaultLiteralOptions options)
    {
        var leftHasDefault = ParameterDefaultValueEmitter.HasDefaultValue(left);
        var rightHasDefault = ParameterDefaultValueEmitter.HasDefaultValue(right);
        if (leftHasDefault != rightHasDefault)
        {
            return false;
        }

        var leftLiteral = ParameterDefaultValueEmitter.FormatSignatureDefaultLiteral(
            left,
            leftHasDefault,
            options) ?? string.Empty;
        var rightLiteral = ParameterDefaultValueEmitter.FormatSignatureDefaultLiteral(
            right,
            rightHasDefault,
            options) ?? string.Empty;
        if (!string.Equals(leftLiteral, rightLiteral, System.StringComparison.Ordinal))
        {
            return false;
        }

        var leftMetadata = ParameterDefaultValueEmitter.FormatMetadataDefaultValueExpression(
            left,
            leftHasDefault,
            leftLiteral);
        var rightMetadata = ParameterDefaultValueEmitter.FormatMetadataDefaultValueExpression(
            right,
            rightHasDefault,
            rightLiteral);
        return string.Equals(leftMetadata, rightMetadata, System.StringComparison.Ordinal);
    }
}
