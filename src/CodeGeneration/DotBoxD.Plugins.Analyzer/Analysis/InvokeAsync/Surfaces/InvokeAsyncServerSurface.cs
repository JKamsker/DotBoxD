using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncServerSurface
{
    private const string InvokeAsyncMethod = "InvokeAsync";

    public static bool TryResolveImplicitGeneratedFacade(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;
        var containingType = model.GetEnclosingSymbol(invocation.SpanStart, cancellationToken)?.ContainingType;
        return containingType is not null &&
               InvokeAsyncReceiverResolver.TryResolveGeneratedFacadeType(
                   containingType,
                   out receiverType,
                   out serverAccessType,
                   out worldType);
    }

    public static bool IsDotBoxDInvokeAsync(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            !string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal))
        {
            return false;
        }

        return IsPluginServerType(method.ContainingType);
    }

    public static bool BindsToUserInvokeAsync(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            method.ContainingType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        return !IsPluginServerType(method.ContainingType);
    }

    public static bool TryResolve(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
        => InvokeAsyncReceiverResolver.TryResolve(
            model,
            receiver,
            cancellationToken,
            out receiverType,
            out serverAccessType,
            out worldType);

    private static bool IsPluginServerType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var original = named.OriginalDefinition.ToDisplayString();
        return string.Equals(original, "DotBoxD.Abstractions.IPluginServer<TWorld>", StringComparison.Ordinal);
    }
}
