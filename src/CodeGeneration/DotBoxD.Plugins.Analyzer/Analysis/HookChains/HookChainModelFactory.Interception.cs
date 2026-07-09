using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static HookChainInterception? Interception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        PluginKernelModel chainModel,
        ExpressionSyntax receiver,
        INamedTypeSymbol eventType,
        IReadOnlyList<HookChainStage> stages,
        string terminalElementTypeFullName,
        GeneratedRemoteHookChainKind? generatedRemoteKind,
        HookChainInterceptorInstallKind installKind,
        string? generatedRemoteServerContextTypeFullName,
        bool terminalHasServerContext,
        bool terminalReturnsVoid,
        bool hasLocalDecoder,
        ITypeSymbol? projectedTypeSymbol,
        out InterceptionFailureReason failureReason,
        CancellationToken cancellationToken)
    {
        failureReason = InterceptionFailureReason.None;
        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            failureReason = InterceptionFailureReason.CallSiteNotInterceptable;
            return null;
        }

        var packageFullName = string.IsNullOrEmpty(chainModel.Namespace)
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + chainModel.PackageName
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + chainModel.Namespace + "." + chainModel.PackageName;

        if (TryKnownReceiverInterception(
                invocation,
                model,
                receiver,
                packageFullName,
                installKind,
                hasLocalDecoder,
                projectedTypeSymbol,
                location.GetInterceptsLocationAttributeSyntax(),
                cancellationToken,
                out var knownInterception))
        {
            return knownInterception;
        }

        if (generatedRemoteKind is null)
        {
            failureReason = InterceptionFailureReason.CallSiteNotInterceptable;
            return null;
        }

        // The generated-remote fallback spells the terminal element by its full type name, but an anonymous
        // projection has no nameable name (terminalElementTypeFullName would be the un-spellable "<anonymous
        // type ...>"). Only the known-stage branch above can emit a generic interceptor that lets Roslyn infer
        // it; decline here so no broken source is emitted (the real RunLocal then fails fast at the call site).
        if (projectedTypeSymbol is INamedTypeSymbol { IsAnonymousType: true })
        {
            failureReason = InterceptionFailureReason.AnonymousGeneratedRemoteProjection;
            return null;
        }

        return GeneratedRemoteHookChainFallback.CreateInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            ExperimentalAttributeSource.FromTypes(eventType, projectedTypeSymbol),
            eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            stages.Any(stage => stage.IsSelect),
            terminalElementTypeFullName,
            generatedRemoteServerContextTypeFullName,
            terminalHasServerContext,
            terminalReturnsVoid,
            packageFullName,
            installKind,
            generatedRemoteKind.Value,
            hasLocalDecoder);
    }

    private static bool TryKnownReceiverInterception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        ExpressionSyntax receiver,
        string packageFullName,
        HookChainInterceptorInstallKind installKind,
        bool hasLocalDecoder,
        ITypeSymbol? projectedTypeSymbol,
        string attributeSyntax,
        CancellationToken cancellationToken,
        out HookChainInterception interception)
    {
        interception = null!;
        if (!TryKnownReceiverContext(invocation, model, receiver, cancellationToken, out var context))
        {
            return false;
        }

        interception = TryAnonymousProjectionInterception(
                context,
                projectedTypeSymbol,
                attributeSyntax,
                packageFullName,
                installKind,
                hasLocalDecoder) ??
            OrdinaryReceiverInterception(context, attributeSyntax, packageFullName, installKind, hasLocalDecoder);
        return true;
    }

    private static bool TryKnownReceiverContext(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out KnownReceiverInterceptionContext context)
    {
        context = default;
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            method.Parameters.Length < 1 ||
            model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol expressionReceiverType)
        {
            return false;
        }

        var receiverType = ResolvedReceiverType(method, expressionReceiverType);
        if (receiverType is null || ReceiverKind(receiverType, model.Compilation) is null)
        {
            return false;
        }

        context = new KnownReceiverInterceptionContext(method, receiverType);
        return true;
    }

    private static HookChainInterception? TryAnonymousProjectionInterception(
        KnownReceiverInterceptionContext context,
        ITypeSymbol? projectedTypeSymbol,
        string attributeSyntax,
        string packageFullName,
        HookChainInterceptorInstallKind installKind,
        bool hasLocalDecoder)
    {
        if (projectedTypeSymbol is not INamedTypeSymbol { IsAnonymousType: true } anonymousProjection ||
            context.Method.Parameters[0].Type is not INamedTypeSymbol handlerType ||
            context.Method.ReturnType is not INamedTypeSymbol returnType)
        {
            return null;
        }

        var receiverType = context.ReceiverType;
        var typeParameters = receiverType.ConstructedFrom.TypeParameters;
        var substitution = TypeParameterSubstitution(receiverType, typeParameters);
        return new HookChainInterception(
            attributeSyntax,
            ExperimentalAttributeSource.FromTypes(receiverType, handlerType, returnType),
            RewriteWithTypeParameters(receiverType, substitution),
            RewriteWithTypeParameters(handlerType, substitution),
            RewriteWithTypeParameters(returnType, substitution),
            packageFullName,
            installKind,
            hasLocalDecoder,
            hasLocalDecoder && substitution.TryGetValue(anonymousProjection, out var decoderTypeArgument)
                ? decoderTypeArgument
                : null,
            string.Join(", ", typeParameters.Select(parameter => parameter.Name)),
            BypassReceiverStage: BypassReceiverStage(receiverType));
    }

    private static Dictionary<ISymbol, string> TypeParameterSubstitution(
        INamedTypeSymbol receiverType,
        IReadOnlyList<ITypeParameterSymbol> typeParameters)
    {
        var substitution = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        for (var i = 0; i < receiverType.TypeArguments.Length && i < typeParameters.Count; i++)
        {
            substitution[receiverType.TypeArguments[i]] = typeParameters[i].Name;
        }

        return substitution;
    }

    private static HookChainInterception OrdinaryReceiverInterception(
        KnownReceiverInterceptionContext context,
        string attributeSyntax,
        string packageFullName,
        HookChainInterceptorInstallKind installKind,
        bool hasLocalDecoder)
        => new(
            attributeSyntax,
            ExperimentalAttributeSource.FromTypes(
                context.ReceiverType,
                context.Method.Parameters[0].Type,
                context.Method.ReturnType),
            context.ReceiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            context.Method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            context.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            packageFullName,
            installKind,
            hasLocalDecoder,
            BypassReceiverStage: BypassReceiverStage(context.ReceiverType));

    private static bool BypassReceiverStage(INamedTypeSymbol receiverType)
    {
        var original = receiverType.OriginalDefinition.ToDisplayString();
        return string.Equals(original, DotBoxDGenerationNames.TypeNames.HookStageWithContextOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.SubscriptionStageWithContextOriginal, StringComparison.Ordinal);
    }

    // The fully-qualified display of <paramref name="type"/> with any type (at any nesting depth) present in
    // <paramref name="substitution"/> replaced by its type-parameter name. Used to spell a generic interceptor's
    // receiver/handler/return when a type argument is an un-nameable anonymous type.
    private static string RewriteWithTypeParameters(ITypeSymbol type, Dictionary<ISymbol, string> substitution)
    {
        if (substitution.TryGetValue(type, out var parameterName))
        {
            return parameterName;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        var prefix = named.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : named.ContainingNamespace.ToDisplayString() + ".";
        var arguments = new List<string>(named.TypeArguments.Length);
        foreach (var argument in named.TypeArguments)
        {
            arguments.Add(RewriteWithTypeParameters(argument, substitution));
        }

        return DotBoxDGenerationNames.TypeNames.GlobalPrefix + prefix + named.Name +
            "<" + string.Join(", ", arguments) + ">";
    }

    private static INamedTypeSymbol? ResolvedReceiverType(IMethodSymbol method, INamedTypeSymbol expressionReceiverType)
        => method.ReceiverType is INamedTypeSymbol receiverType && receiverType.TypeKind != TypeKind.Error
            ? receiverType
            : expressionReceiverType;

    private static string InterceptionFailureDetail(InterceptionFailureReason reason)
        => reason switch
        {
            InterceptionFailureReason.AnonymousGeneratedRemoteProjection =>
                "anonymous terminal projections on generated-server chains require a named record projection",
            _ => "the call site is not interceptable"
        };

    private readonly record struct KnownReceiverInterceptionContext(
        IMethodSymbol Method,
        INamedTypeSymbol ReceiverType);

    private enum InterceptionFailureReason
    {
        None,
        CallSiteNotInterceptable,
        AnonymousGeneratedRemoteProjection
    }
}
