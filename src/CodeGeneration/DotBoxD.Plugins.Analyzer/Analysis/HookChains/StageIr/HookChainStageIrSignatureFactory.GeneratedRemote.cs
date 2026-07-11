using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainStageIrGeneratedRemoteSignatureFactory
{
    internal static HookChainStageIrSignature Create(
        HookChainStage stage,
        ITypeSymbol inputType,
        string outputTypeFullName,
        INamedTypeSymbol eventType,
        GeneratedRemoteHookChainKind kind,
        string? serverContextTypeFullName,
        bool receiverIsStage)
    {
        var eventTypeFullName = TypeFullName(eventType);
        var inputTypeFullName = TypeFullName(inputType);
        var contextTypeFullName = string.IsNullOrEmpty(serverContextTypeFullName)
            ? null
            : serverContextTypeFullName;
        var receiverType = receiverIsStage
            ? StageType(kind, eventTypeFullName, inputTypeFullName, contextTypeFullName)
            : PipelineType(kind, eventTypeFullName, contextTypeFullName);
        var returnType = stage.IsSelect || receiverIsStage
            ? StageType(kind, eventTypeFullName, stage.IsSelect ? outputTypeFullName : inputTypeFullName, contextTypeFullName)
            : PipelineType(kind, eventTypeFullName, contextTypeFullName);
        var hasContextParameter = HasContextParameter(stage);
        var delegateContextTypeFullName = contextTypeFullName ?? DotBoxDGenerationNames.TypeNames.GlobalHookContext;

        return new HookChainStageIrSignature(
            receiverType,
            DelegateType(inputTypeFullName, outputTypeFullName, hasContextParameter, delegateContextTypeFullName),
            returnType,
            IRFuncType(inputTypeFullName, outputTypeFullName, hasContextParameter, delegateContextTypeFullName),
            stage.IsSelect ? "<" + outputTypeFullName + ">" : string.Empty,
            null,
            string.Empty,
            stage.IsSelect ? "Select" : "Where",
            stage.IsSelect ? "projection" : "filter",
            stage.IsSelect ? "irProjection" : "irFilter");
    }

    private static string PipelineType(
        GeneratedRemoteHookChainKind kind,
        string eventTypeFullName,
        string? serverContextTypeFullName)
    {
        var typeName = kind == GeneratedRemoteHookChainKind.Hook
            ? "DotBoxD.Plugins.Runtime.RemoteHookPipeline"
            : "DotBoxD.Plugins.Runtime.RemoteSubscriptionPipeline";
        var typeArguments = serverContextTypeFullName is null
            ? eventTypeFullName
            : eventTypeFullName + ", " + serverContextTypeFullName;
        return DotBoxDGenerationNames.TypeNames.GlobalPrefix + typeName + "<" + typeArguments + ">";
    }

    private static string StageType(
        GeneratedRemoteHookChainKind kind,
        string eventTypeFullName,
        string currentTypeFullName,
        string? serverContextTypeFullName)
    {
        var typeName = kind == GeneratedRemoteHookChainKind.Hook
            ? "DotBoxD.Plugins.Runtime.Hooks.RemoteHookStage"
            : "DotBoxD.Plugins.Runtime.Subscriptions.RemoteSubscriptionStage";
        var typeArguments = eventTypeFullName + ", " + currentTypeFullName +
            (serverContextTypeFullName is null ? string.Empty : ", " + serverContextTypeFullName);
        return DotBoxDGenerationNames.TypeNames.GlobalPrefix + typeName + "<" + typeArguments + ">";
    }

    private static string DelegateType(
        string inputTypeFullName,
        string outputTypeFullName,
        bool hasContextParameter,
        string contextTypeFullName)
        => hasContextParameter
            ? DotBoxDGenerationNames.TypeNames.GlobalFunc + "<" +
              inputTypeFullName + ", " + contextTypeFullName + ", " + outputTypeFullName + ">"
            : DotBoxDGenerationNames.TypeNames.GlobalFunc + "<" +
              inputTypeFullName + ", " + outputTypeFullName + ">";

    private static string IRFuncType(
        string inputTypeFullName,
        string outputTypeFullName,
        bool hasContextParameter,
        string contextTypeFullName)
        => hasContextParameter
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + "DotBoxD.Abstractions.IRFunc<" +
              inputTypeFullName + ", " + contextTypeFullName + ", " + outputTypeFullName + ">"
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + "DotBoxD.Abstractions.IRFunc<" +
              inputTypeFullName + ", " + outputTypeFullName + ">";

    private static bool HasContextParameter(HookChainStage stage)
        => stage.Lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 };

    private static string TypeFullName(ITypeSymbol type)
    {
        if (ContainsAnonymousType(type))
        {
            throw new NotSupportedException("anonymous generated remote stage types are not supported.");
        }

        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static bool ContainsAnonymousType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsAnonymousType: true })
        {
            return true;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (ContainsAnonymousType(argument))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
