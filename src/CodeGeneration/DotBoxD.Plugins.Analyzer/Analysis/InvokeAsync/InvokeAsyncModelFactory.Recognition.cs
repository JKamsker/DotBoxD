using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncModelFactory
{
    private static IMethodSymbol? MarkedMethod(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        return LowerToIrMethodReader.IsAnonymousInvocation(method, model.Compilation)
            ? method
            : null;
    }

    private static bool IsUnqualifiedInvocationExpression(ExpressionSyntax expression)
        => expression is IdentifierNameSyntax or GenericNameSyntax;

    private static bool IsConditionalInvocationExpression(ExpressionSyntax expression)
        => expression is MemberBindingExpressionSyntax;

    private static bool IsInvokeAsyncName(ExpressionSyntax expression)
        => expression switch
        {
            IdentifierNameSyntax identifier => IsInvokeAsyncName(identifier),
            GenericNameSyntax generic => IsInvokeAsyncName(generic),
            MemberBindingExpressionSyntax binding => IsInvokeAsyncName(binding.Name),
            _ => false
        };

    private static bool IsInvokeAsyncName(SimpleNameSyntax name)
        => string.Equals(name.Identifier.ValueText, InvokeAsyncMethod, StringComparison.Ordinal);

    private static bool IsDotBoxDLoweredInvocation(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return LowerToIrMethodReader.IsAnonymousInvocation(method, model.Compilation) ||
            string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal) &&
            IsPluginServerType(method.ContainingType);
    }

    private static bool BindsToUnmarkedUserInvocation(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            method.ContainingType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (LowerToIrMethodReader.IsAnonymousInvocation(method, model.Compilation))
        {
            return false;
        }

        return !IsPluginServerType(method.ContainingType);
    }

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
