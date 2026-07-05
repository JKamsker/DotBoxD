using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private const string RegistryAttributeName =
        "DotBoxD.Abstractions.GeneratedPluginServerRegistryAttribute";

    public static GeneratedRemoteHookChainTarget? Candidate(
        InvocationExpressionSyntax seed,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (seed.Expression is not MemberAccessExpressionSyntax onAccess ||
            !string.Equals(onAccess.Name.Identifier.ValueText, "On", StringComparison.Ordinal))
        {
            return null;
        }

        return RegistryTarget(onAccess.Expression, model, cancellationToken, depth: 0);
    }

    private static GeneratedRemoteHookChainTarget? RegistryTarget(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        var context = new RegistryTargetContext(expression, model, cancellationToken, depth);
        foreach (var resolver in RegistryTargetResolvers)
        {
            if (resolver(context) is { } target)
            {
                return target;
            }
        }

        return null;
    }

    private static GeneratedRemoteHookChainTarget? TargetFromAssignmentRegistryExpression(
        AssignmentExpressionSyntax assignment,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return RegistryTarget(assignment.Right, model, cancellationToken, depth + 1);
        }

        if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            return RegistryTarget(assignment.Right, model, cancellationToken, depth + 1);
        }

        return null;
    }

    private static GeneratedRemoteHookChainTarget? TargetFromGeneratedServerMember(
        MemberAccessExpressionSyntax registryAccess,
        SemanticModel model,
        CancellationToken cancellationToken)
        => TargetFromGeneratedServerMember(
            registryAccess.Name,
            registryAccess.Expression,
            registryAccess,
            model,
            cancellationToken);

    private static GeneratedRemoteHookChainTarget? TargetFromConditionalAccessGeneratedServerMember(
        ConditionalAccessExpressionSyntax registryAccess,
        SemanticModel model,
        CancellationToken cancellationToken)
        => registryAccess.WhenNotNull is MemberBindingExpressionSyntax binding
            ? TargetFromGeneratedServerMember(
                binding.Name,
                registryAccess.Expression,
                binding,
                model,
                cancellationToken)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromGeneratedServerMember(
        SimpleNameSyntax registryName,
        ExpressionSyntax serverExpressionSyntax,
        ExpressionSyntax registryExpression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!TryRemoteHookChainKind(registryName, out var kind))
        {
            return null;
        }

        var serverExpression = HookChainAliasResolver.UnwrapTransparentExpression(serverExpressionSyntax);
        string? context = null;
        INamedTypeSymbol? contextType = null;
        if (TryResolveAttributedServerTarget(
                kind,
                serverExpression,
                registryExpression,
                model,
                cancellationToken,
                out var attributedTarget,
                out context,
                out contextType))
        {
            return attributedTarget;
        }

        context ??= ContextFromOwnedGeneratedServerExpression(serverExpression, model, cancellationToken);
        if (context is null)
        {
            return null;
        }

        return new GeneratedRemoteHookChainTarget(kind, context, contextType);
    }

    private static bool TryRemoteHookChainKind(
        SimpleNameSyntax registryName,
        out GeneratedRemoteHookChainKind kind)
    {
        switch (registryName.Identifier.ValueText)
        {
            case "Hooks":
                kind = GeneratedRemoteHookChainKind.Hook;
                return true;
            case "Subscriptions":
                kind = GeneratedRemoteHookChainKind.Subscription;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryResolveAttributedServerTarget(
        GeneratedRemoteHookChainKind kind,
        ExpressionSyntax serverExpression,
        ExpressionSyntax registryExpression,
        SemanticModel model,
        CancellationToken cancellationToken,
        out GeneratedRemoteHookChainTarget? target,
        out string? context,
        out INamedTypeSymbol? contextType)
    {
        target = null;
        context = null;
        contextType = null;
        if (model.GetTypeInfo(serverExpression, cancellationToken).Type is not INamedTypeSymbol serverType ||
            !HasGeneratePluginServerAttribute(serverType, model.Compilation))
        {
            return false;
        }

        contextType = GeneratedContextType(serverType, model.Compilation);
        if (contextType is null)
        {
            return true;
        }

        context = contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (model.GetSymbolInfo(registryExpression, cancellationToken).Symbol is IPropertySymbol property &&
            property.Type is INamedTypeSymbol registryType)
        {
            target = MatchingRegistryMarker(kind, registryType, context, model.Compilation);
            return true;
        }

        return false;
    }

    private static GeneratedRemoteHookChainTarget? MatchingRegistryMarker(
        GeneratedRemoteHookChainKind kind,
        INamedTypeSymbol registryType,
        string context,
        Compilation compilation)
        => TargetFromRegistryMarker(registryType, compilation) is { } marked &&
           marked.Kind == kind &&
           string.Equals(marked.ServerContextTypeFullName, context, StringComparison.Ordinal)
            ? marked
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromDeclaredRegistryExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => DeclaredTypeSyntax(expression, model, cancellationToken) is { } typeSyntax
            ? TargetFromOwnedGeneratedRegistryType(typeSyntax, model, cancellationToken)
            : null;

    private static string? ContextFromOwnedGeneratedServerExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => DeclaredTypeSyntax(expression, model, cancellationToken) is { } typeSyntax
            ? ContextFromOwnedGeneratedServerType(typeSyntax, model, cancellationToken)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromOwnedGeneratedRegistryType(
        TypeSyntax typeSyntax,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var resolvedType = TypeFromSyntax(typeSyntax, model, cancellationToken);
        if (resolvedType is { TypeKind: not TypeKind.Error })
        {
            return resolvedType is INamedTypeSymbol registryType
                ? TargetFromRegistryMarker(registryType, model.Compilation)
                : null;
        }

        foreach (var surface in OwnedGeneratedSurfaces(model.Compilation, cancellationToken))
        {
            if (TypeMatches(
                typeSyntax,
                surface.HookRegistryName,
                surface.HookRegistryFullName,
                model,
                cancellationToken))
            {
                return new GeneratedRemoteHookChainTarget(
                    GeneratedRemoteHookChainKind.Hook,
                    surface.ContextFullName,
                    surface.ContextType);
            }

            if (TypeMatches(
                typeSyntax,
                surface.SubscriptionRegistryName,
                surface.SubscriptionRegistryFullName,
                model,
                cancellationToken))
            {
                return new GeneratedRemoteHookChainTarget(
                    GeneratedRemoteHookChainKind.Subscription,
                    surface.ContextFullName,
                    surface.ContextType);
            }
        }

        return null;
    }

    private static string? ContextFromOwnedGeneratedServerType(
        TypeSyntax typeSyntax,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var resolvedType = TypeFromSyntax(typeSyntax, model, cancellationToken);
        if (resolvedType is { TypeKind: not TypeKind.Error })
        {
            return resolvedType is INamedTypeSymbol serverType
                ? GeneratedContextTypeFullName(serverType, model.Compilation)
                : null;
        }

        foreach (var surface in OwnedGeneratedSurfaces(model.Compilation, cancellationToken))
        {
            if (TypeMatches(
                typeSyntax,
                surface.ServerInterfaceName,
                surface.ServerInterfaceFullName,
                model,
                cancellationToken))
            {
                return surface.ContextFullName;
            }
        }

        return null;
    }

}
