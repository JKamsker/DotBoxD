using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal delegate string RequiredRpcExpressionLowerer(
    ExpressionSyntax expression,
    ITypeSymbol targetType,
    string description);

internal delegate string? RpcAssignmentOverride(
    AssignmentExpressionSyntax assignment,
    RequiredRpcExpressionLowerer lowerRequired);

internal sealed partial class DotBoxDRpcJsonLowerer
{
    internal string LowerRequiredExpression(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        string description)
        => ApplyRequiredNumericConversion(
            expression,
            targetType,
            LowerExpression(expression),
            description);

    internal string LowerRequiredExpressionWithPrelude(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        string description,
        List<string> output)
        => ApplyRequiredNumericConversion(
            expression,
            targetType,
            LowerExpressionWithPrelude(expression, output),
            description);
}
