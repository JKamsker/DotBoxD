using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static void RejectUnsupportedProjectedType(
        HookChainInterceptorInstallKind installKind,
        ITypeSymbol? projectedType,
        SimpleNameSyntax terminalName)
    {
        if (installKind == HookChainInterceptorInstallKind.LocalCallback)
        {
            RejectFileLocalProjectedType(projectedType, terminalName);
        }
    }

    private static void RejectFileLocalProjectedType(ITypeSymbol? projectedType, SimpleNameSyntax terminalName)
    {
        if (projectedType is null ||
            FindFileLocalType(projectedType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) is not { } fileLocalType)
        {
            return;
        }

        var message = "Remote RunLocal projected payload type '" +
            fileLocalType.ToDisplayString() +
            "' is file-local; generated hook-chain sources cannot name file-local types. " +
            "Use a named payload type that is visible to generated code, or project to supported scalar/record fields.";
        throw new HookChainUnsupportedDiagnosticException(
            new PluginKernelDiagnostic(message, PluginDiagnosticLocation.From(terminalName.GetLocation())));
    }

    private static void RejectUnsupportedServerContextType(
        ITypeSymbol? contextType,
        InvocationExpressionSyntax seed,
        SimpleNameSyntax terminalName)
    {
        if (contextType is null ||
            FindFileLocalType(contextType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) is not { } fileLocalType)
        {
            return;
        }

        var message = "Hook chain server context type '" +
            fileLocalType.ToDisplayString() +
            "' is file-local; generated hook-chain sources cannot name file-local types. " +
            "Use a named context type that is visible to generated code, or use the default HookContext.";
        throw new HookChainUnsupportedDiagnosticException(
            new PluginKernelDiagnostic(
                message,
                ServerContextTypeLocation(seed) ?? PluginDiagnosticLocation.From(terminalName.GetLocation())));
    }

    private static ITypeSymbol? ServerContextType(
        SemanticModel model,
        ExpressionSyntax receiver,
        InvocationExpressionSyntax seed,
        GeneratedRemoteHookChainTarget? generatedRemoteTarget,
        CancellationToken cancellationToken)
    {
        if (generatedRemoteTarget is { } target)
        {
            return GeneratedRemoteHookChainFallback.ServerContextType(model, seed, target, cancellationToken);
        }

        return model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol receiverType
            ? ReceiverServerContextType(receiverType)
            : null;
    }

    private static ITypeSymbol? ReceiverServerContextType(INamedTypeSymbol receiverType)
    {
        var original = receiverType.OriginalDefinition.ToDisplayString();
        return original switch
        {
            DotBoxDGenerationNames.TypeNames.HookPipelineWithContextOriginal or
            DotBoxDGenerationNames.TypeNames.HookStageWithContextOriginal or
            DotBoxDGenerationNames.TypeNames.RemoteHookPipelineWithContextOriginal or
            DotBoxDGenerationNames.TypeNames.RemoteHookStageWithContextOriginal or
            DotBoxDGenerationNames.TypeNames.SubscriptionPipelineWithContextOriginal or
            DotBoxDGenerationNames.TypeNames.SubscriptionStageWithContextOriginal or
            DotBoxDGenerationNames.TypeNames.RemoteSubscriptionPipelineWithContextOriginal or
            DotBoxDGenerationNames.TypeNames.RemoteSubscriptionStageWithContextOriginal =>
                receiverType.TypeArguments[receiverType.TypeArguments.Length - 1],
            _ => null,
        };
    }

    private static PluginDiagnosticLocation? ServerContextTypeLocation(InvocationExpressionSyntax seed)
    {
        if (seed.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax onName } &&
            onName.TypeArgumentList.Arguments.Count > 1)
        {
            return PluginDiagnosticLocation.From(onName.TypeArgumentList.Arguments[1].GetLocation());
        }

        return null;
    }

    private static INamedTypeSymbol? FindFileLocalType(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            return null;
        }

        return type switch
        {
            IArrayTypeSymbol arrayType => FindFileLocalType(arrayType.ElementType, visited),
            INamedTypeSymbol namedType => FindFileLocalNamedType(namedType, visited),
            _ => null
        };
    }

    private static INamedTypeSymbol? FindFileLocalNamedType(INamedTypeSymbol namedType, HashSet<ITypeSymbol> visited)
    {
        if (IsFileLocal(namedType))
        {
            return namedType;
        }

        if (FindFileLocalTypeArgument(namedType, visited) is { } fileLocalType)
        {
            return fileLocalType;
        }

        return namedType.IsAnonymousType
            ? FindFileLocalAnonymousMemberType(namedType, visited)
            : null;
    }

    private static INamedTypeSymbol? FindFileLocalTypeArgument(INamedTypeSymbol namedType, HashSet<ITypeSymbol> visited)
    {
        foreach (var typeArgument in namedType.TypeArguments)
        {
            if (FindFileLocalType(typeArgument, visited) is { } fileLocalType)
            {
                return fileLocalType;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? FindFileLocalAnonymousMemberType(
        INamedTypeSymbol namedType,
        HashSet<ITypeSymbol> visited)
    {
        foreach (var member in namedType.GetMembers())
        {
            if (member is IPropertySymbol { IsStatic: false } property &&
                FindFileLocalType(property.Type, visited) is { } fileLocalType)
            {
                return fileLocalType;
            }
        }

        return null;
    }

    private static bool IsFileLocal(INamedTypeSymbol type)
    {
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not SyntaxNode declaration)
            {
                continue;
            }

            var modifiers = declaration switch
            {
                BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.Modifiers,
                DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Modifiers,
                _ => default,
            };
            foreach (var modifier in modifiers)
            {
                if (modifier.IsKind(SyntaxKind.FileKeyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed class HookChainUnsupportedDiagnosticException(PluginKernelDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public PluginKernelDiagnostic Diagnostic { get; } = diagnostic;
    }
}
