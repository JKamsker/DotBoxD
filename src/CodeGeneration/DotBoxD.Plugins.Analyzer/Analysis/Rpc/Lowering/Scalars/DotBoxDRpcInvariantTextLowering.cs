using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string? TryLowerInvariantInt32Text(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member ||
            ModelFor(invocation).GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol
            { Name: "ToString", IsStatic: false, ContainingType.SpecialType: SpecialType.System_Int32 } ||
            invocation.ArgumentList.Arguments.Count != 1)
        { return null; }
        var provider = invocation.ArgumentList.Arguments[0].Expression;
        if (ModelFor(provider).GetSymbolInfo(provider, _cancellationToken).Symbol is not IPropertySymbol
            { Name: "InvariantCulture" } culture || culture.ContainingType.ToDisplayString() != "System.Globalization.CultureInfo")
        { return null; }
        Allocates = true;
        return Call("int32.toStringInvariant", null, [LowerExpression(member.Expression)]);
    }
}
