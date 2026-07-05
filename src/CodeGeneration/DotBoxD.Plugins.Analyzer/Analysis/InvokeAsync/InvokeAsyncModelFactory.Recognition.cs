using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncModelFactory
{
    private const string PluginServerInterfaceMetadataName = "DotBoxD.Abstractions.IPluginServer`1";

    private static IMethodSymbol? MarkedMethod(IMethodSymbol? method, Compilation compilation)
        => LowerToIrMethodReader.IsAnonymousInvocation(method, compilation)
            ? method
            : null;

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
        IMethodSymbol? method,
        Compilation compilation,
        INamedTypeSymbol? pluginServerType)
        => method is not null &&
           (LowerToIrMethodReader.IsAnonymousInvocation(method, compilation) ||
            (string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal) &&
             IsPluginServerType(method.ContainingType, pluginServerType)));

    private static bool BindsToUnmarkedUserInvocation(
        IMethodSymbol? method,
        Compilation compilation,
        INamedTypeSymbol? pluginServerType)
    {
        if (method is null || method.ContainingType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (LowerToIrMethodReader.IsAnonymousInvocation(method, compilation))
        {
            return false;
        }

        return !IsPluginServerType(method.ContainingType, pluginServerType);
    }

    private static bool IsPluginServerType(ITypeSymbol? type, INamedTypeSymbol? pluginServerType)
    {
        if (type is not INamedTypeSymbol named || pluginServerType is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, pluginServerType);
    }
}
