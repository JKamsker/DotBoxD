using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal enum GeneratedRemoteHookChainKind
{
    Hook,
    Subscription
}

internal static class GeneratedRemoteHookChainFallback
{
    public static GeneratedRemoteHookChainKind? CandidateKind(InvocationExpressionSyntax seed)
    {
        if (seed.Expression is not MemberAccessExpressionSyntax onAccess ||
            !string.Equals(onAccess.Name.Identifier.ValueText, "On", StringComparison.Ordinal) ||
            onAccess.Expression is not MemberAccessExpressionSyntax registryAccess)
        {
            return null;
        }

        return registryAccess.Name.Identifier.ValueText switch
        {
            "Hooks" => GeneratedRemoteHookChainKind.Hook,
            "Subscriptions" => GeneratedRemoteHookChainKind.Subscription,
            _ => null
        };
    }

    private static INamedTypeSymbol? EventTypeFromSeed(
        InvocationExpressionSyntax seed,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (seed.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax onName } ||
            onName.TypeArgumentList.Arguments.Count < 1)
        {
            return null;
        }

        return model.GetTypeInfo(onName.TypeArgumentList.Arguments[0], cancellationToken).Type as INamedTypeSymbol;
    }

    public static bool TryEventType(
        SemanticModel model,
        InvocationExpressionSyntax seed,
        CancellationToken cancellationToken,
        out INamedTypeSymbol eventType)
    {
        if (model.GetTypeInfo(seed, cancellationToken).Type is INamedTypeSymbol pipelineType &&
            pipelineType.TypeArguments.Length >= 1 &&
            pipelineType.TypeArguments[0] is INamedTypeSymbol semanticEventType)
        {
            eventType = semanticEventType;
            return true;
        }

        if (EventTypeFromSeed(seed, model, cancellationToken) is { } syntaxEventType)
        {
            eventType = syntaxEventType;
            return true;
        }

        eventType = null!;
        return false;
    }

    public static string? ServerContextTypeFullName(
        SemanticModel model,
        InvocationExpressionSyntax seed,
        CancellationToken cancellationToken)
    {
        if (seed.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax onName } ||
            onName.TypeArgumentList.Arguments.Count < 2)
        {
            return null;
        }

        return model.GetTypeInfo(onName.TypeArgumentList.Arguments[1], cancellationToken)
            .Type?
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static HookChainInterception CreateInterception(
        string attributeSyntax,
        string eventTypeFullName,
        bool receiverIsStage,
        string terminalElementTypeFullName,
        string? serverContextTypeFullName,
        bool terminalHasServerContext,
        string packageFullName,
        HookChainInterceptorInstallKind installKind,
        GeneratedRemoteHookChainKind kind,
        bool hasLocalDecoder)
    {
        var pipelineName = kind == GeneratedRemoteHookChainKind.Hook
            ? "DotBoxD.Plugins.Runtime.RemoteHookPipeline"
            : "DotBoxD.Plugins.Runtime.RemoteSubscriptionPipeline";
        var stageName = kind == GeneratedRemoteHookChainKind.Hook
            ? "DotBoxD.Plugins.Runtime.Hooks.RemoteHookStage"
            : "DotBoxD.Plugins.Runtime.Subscriptions.RemoteSubscriptionStage";
        var hasServerContextType = !string.IsNullOrEmpty(serverContextTypeFullName);
        var pipelineArguments = hasServerContextType
            ? eventTypeFullName + ", " + serverContextTypeFullName
            : eventTypeFullName;
        var pipelineType = DotBoxDGenerationNames.TypeNames.GlobalPrefix +
            pipelineName + "<" + pipelineArguments + ">";
        var receiverType = receiverIsStage
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix +
              stageName + "<" + eventTypeFullName + ", " + terminalElementTypeFullName +
              (hasServerContextType ? ", " + serverContextTypeFullName : string.Empty) + ">"
            : pipelineType;
        var handlerType = HandlerType(
            terminalElementTypeFullName,
            terminalHasServerContext
                ? serverContextTypeFullName ?? DotBoxDGenerationNames.TypeNames.GlobalHookContext
                : null,
            installKind);

        return new HookChainInterception(
            attributeSyntax,
            receiverType,
            handlerType,
            pipelineType,
            packageFullName,
            installKind,
            hasLocalDecoder);
    }

    private static string HandlerType(
        string terminalElementTypeFullName,
        string? serverContextTypeFullName,
        HookChainInterceptorInstallKind installKind)
    {
        var hasServerContext = !string.IsNullOrEmpty(serverContextTypeFullName);
        if (installKind == HookChainInterceptorInstallKind.LocalCallback)
        {
            return hasServerContext
                ? DotBoxDGenerationNames.TypeNames.GlobalFunc + "<" +
                  terminalElementTypeFullName + ", " + serverContextTypeFullName + ", " +
                  DotBoxDGenerationNames.TypeNames.GlobalValueTask + ">"
                : DotBoxDGenerationNames.TypeNames.GlobalFunc + "<" +
                  terminalElementTypeFullName + ", " + DotBoxDGenerationNames.TypeNames.GlobalValueTask + ">";
        }

        return hasServerContext
            ? DotBoxDGenerationNames.TypeNames.GlobalAction + "<" +
              terminalElementTypeFullName + ", " + serverContextTypeFullName + ">"
            : DotBoxDGenerationNames.TypeNames.GlobalAction + "<" + terminalElementTypeFullName + ">";
    }

    public static HookChainInterception CreateResultInterception(
        string attributeSyntax,
        string eventTypeFullName,
        bool receiverIsStage,
        string resultTypeFullName,
        string packageFullName,
        HookChainInterceptorInstallKind installKind,
        GeneratedRemoteHookChainKind kind,
        bool isAsyncLocal)
    {
        if (kind != GeneratedRemoteHookChainKind.Hook)
        {
            throw new NotSupportedException("Result hooks are only supported on generated remote hook registries.");
        }

        var pipelineType = DotBoxDGenerationNames.TypeNames.GlobalPrefix +
            "DotBoxD.Plugins.Runtime.RemoteHookPipeline<" + eventTypeFullName + ">";
        var receiverType = receiverIsStage
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix +
              "DotBoxD.Plugins.Runtime.Hooks.RemoteHookStage<" +
              eventTypeFullName + ", " + eventTypeFullName + ">"
            : pipelineType;
        var handlerType = installKind == HookChainInterceptorInstallKind.ResultChain
            ? DotBoxDGenerationNames.TypeNames.GlobalFunc + "<" + eventTypeFullName + ", " + resultTypeFullName + ">"
            : isAsyncLocal
            ? DotBoxDGenerationNames.TypeNames.GlobalFunc + "<" +
              eventTypeFullName + ", " + DotBoxDGenerationNames.TypeNames.GlobalHookContext + ", " +
              DotBoxDGenerationNames.TypeNames.GlobalCancellationToken + ", " +
              DotBoxDGenerationNames.TypeNames.GlobalValueTask + "<" + resultTypeFullName + ">>"
            : DotBoxDGenerationNames.TypeNames.GlobalFunc + "<" +
              eventTypeFullName + ", " + DotBoxDGenerationNames.TypeNames.GlobalHookContext + ", " +
              resultTypeFullName + ">";

        return new HookChainInterception(
            attributeSyntax,
            receiverType,
            handlerType,
            pipelineType,
            packageFullName,
            installKind,
            ResultTypeFullName: resultTypeFullName,
            IsAsyncLocalResult: isAsyncLocal);
    }

    public static string TypeFullName(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        string sandboxType)
    {
        var typeInfo = model.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type is not null && type.TypeKind != TypeKind.Error)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return sandboxType switch
        {
            DotBoxDGenerationNames.ManifestTypes.Bool => "global::System.Boolean",
            DotBoxDGenerationNames.ManifestTypes.Int => "global::System.Int32",
            DotBoxDGenerationNames.ManifestTypes.Long => "global::System.Int64",
            DotBoxDGenerationNames.ManifestTypes.Double => "global::System.Double",
            DotBoxDGenerationNames.ManifestTypes.String => "global::System.String",
            _ => throw new NotSupportedException($"Unsupported projected hook element type '{sandboxType}'."),
        };
    }
}
