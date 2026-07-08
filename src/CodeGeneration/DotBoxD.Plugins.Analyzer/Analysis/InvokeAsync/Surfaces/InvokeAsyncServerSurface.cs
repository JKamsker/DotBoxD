using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncServerSurface
{
    private const string InvokeAsyncMethod = "InvokeAsync";
    private const string PluginServerInterfaceMetadataName = "DotBoxD.Abstractions.IPluginServer`1";

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
        if (ResolvedMethod(model, invocation, cancellationToken) is not { } method)
        {
            return false;
        }

        if (!string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsPluginServerType(method.ContainingType, model.Compilation))
        {
            return true;
        }

        return LowerToIrMethodReader.IsAnonymousInvocation(method, model.Compilation) &&
            IsGeneratedPluginServerFacadeType(method.ContainingType);
    }

    public static bool BindsToUserInvokeAsync(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (ResolvedMethod(model, invocation, cancellationToken) is not { } method ||
            method.ContainingType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        return !LowerToIrMethodReader.IsAnonymousInvocation(method, model.Compilation) &&
            !IsPluginServerType(method.ContainingType, model.Compilation);
    }

    public static bool IsLoweringCandidate(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (ResolvedMethod(model, invocation, cancellationToken) is { } method)
        {
            return LowerToIrMethodReader.IsAnonymousInvocation(method, model.Compilation) ||
                string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal);
        }

        return InvocationName(invocation) is { } name &&
            string.Equals(name.Identifier.ValueText, InvokeAsyncMethod, StringComparison.Ordinal);
    }

    public static IMethodSymbol? ResolvedMethod(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        return info.Symbol as IMethodSymbol ??
            (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] as IMethodSymbol : null);
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

    private static bool IsPluginServerType(ITypeSymbol? type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol named ||
            compilation.GetTypeByMetadataName(PluginServerInterfaceMetadataName) is not { } pluginServerType)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, pluginServerType);
    }

    private static bool IsGeneratedPluginServerFacadeType(ITypeSymbol? type)
        => type is INamedTypeSymbol named &&
           InvokeAsyncReceiverResolver.TryResolveGeneratedFacadeType(named, out _, out _, out _);

    private static SimpleNameSyntax? InvocationName(InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier,
            GenericNameSyntax generic => generic,
            MemberAccessExpressionSyntax access => access.Name,
            MemberBindingExpressionSyntax binding => binding.Name,
            _ => null,
        };
}
