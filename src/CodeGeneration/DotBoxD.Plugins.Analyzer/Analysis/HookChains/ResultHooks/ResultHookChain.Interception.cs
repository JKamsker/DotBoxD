using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class ResultHookChain
{
    private static HookChainInterception? Interception(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver,
        SemanticModel model,
        PluginKernelModel kernelModel,
        INamedTypeSymbol contextType,
        INamedTypeSymbol resultType,
        bool isLocal,
        bool hasServerContextParameter,
        bool isAsyncLocal,
        bool hasCancellationToken,
        bool receiverIsStage,
        GeneratedRemoteHookChainKind? generatedRemoteKind,
        string? generatedRemoteServerContextTypeFullName,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var packageFullName = string.IsNullOrEmpty(kernelModel.Namespace)
            ? TypeNames.GlobalPrefix + kernelModel.PackageName
            : TypeNames.GlobalPrefix + kernelModel.Namespace + "." + kernelModel.PackageName;

        var contextFullName = contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var resultFullName = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var serverContextFullName = ServerContextFullName(
            receiver,
            model,
            cancellationToken,
            generatedRemoteServerContextTypeFullName);
        var handlerFullName = ResultHandlerFullName(
            contextFullName,
            serverContextFullName,
            resultFullName,
            hasServerContextParameter,
            isAsyncLocal,
            hasCancellationToken);
        var installKind = ResultInstallKind(isLocal);

        if (TryCreateSymbolResolvedInterception(
                invocation,
                receiver,
                model,
                location,
                packageFullName,
                resultFullName,
                installKind,
                cancellationToken,
                out var symbolResolvedInterception))
        {
            return symbolResolvedInterception;
        }

        if (ValidReceiverType(receiver, model, cancellationToken) is not { } receiverType)
        {
            return CreateGeneratedResultInterception(
                location,
                ExperimentalAttributeSource.FromTypes(contextType, resultType),
                contextFullName,
                receiverIsStage,
                resultFullName,
                generatedRemoteServerContextTypeFullName,
                hasServerContextParameter,
                isAsyncLocal,
                hasCancellationToken,
                packageFullName,
                installKind,
                generatedRemoteKind);
        }

        var receiverFullName = receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new HookChainInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            ExperimentalAttributeSource.FromTypes(receiverType, contextType, resultType),
            receiverFullName,
            handlerFullName,
            receiverFullName,
            packageFullName,
            installKind,
            ResultTypeFullName: resultFullName);
    }

    private static bool TryCreateSymbolResolvedInterception(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver,
        SemanticModel model,
        InterceptableLocation location,
        string packageFullName,
        string resultFullName,
        HookChainInterceptorInstallKind installKind,
        CancellationToken cancellationToken,
        out HookChainInterception? interception)
    {
        interception = null;
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        if (method.Parameters.Length < 1)
        {
            return false;
        }

        if (method.Parameters[0].Type is not INamedTypeSymbol handlerType)
        {
            return false;
        }

        var resolvedReceiver = ResolvedReceiverType(method, model.GetTypeInfo(receiver, cancellationToken).Type);
        if (resolvedReceiver is null)
        {
            return false;
        }

        interception = new HookChainInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            ExperimentalAttributeSource.FromTypes(resolvedReceiver, handlerType, method.ReturnType),
            resolvedReceiver.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            packageFullName,
            installKind,
            ResultTypeFullName: resultFullName);
        return true;
    }

    private static INamedTypeSymbol? ValidReceiverType(
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol receiverType)
        {
            return null;
        }

        return receiverType.TypeKind == TypeKind.Error ? null : receiverType;
    }

    private static HookChainInterception? CreateGeneratedResultInterception(
        InterceptableLocation location,
        string generatedAttributeSource,
        string contextFullName,
        bool receiverIsStage,
        string resultFullName,
        string? generatedRemoteServerContextTypeFullName,
        bool hasServerContextParameter,
        bool isAsyncLocal,
        bool hasCancellationToken,
        string packageFullName,
        HookChainInterceptorInstallKind installKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
    {
        if (generatedRemoteKind is null)
        {
            return null;
        }

        return GeneratedRemoteHookChainFallback.CreateResultInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            generatedAttributeSource,
            contextFullName,
            receiverIsStage,
            resultFullName,
            generatedRemoteServerContextTypeFullName,
            hasServerContextParameter,
            isAsyncLocal,
            hasCancellationToken,
            packageFullName,
            installKind,
            generatedRemoteKind.Value);
    }

    private static HookChainInterceptorInstallKind ResultInstallKind(bool isLocal)
        => isLocal ? HookChainInterceptorInstallKind.LocalResultChain : HookChainInterceptorInstallKind.ResultChain;

    private static INamedTypeSymbol? ResolvedReceiverType(IMethodSymbol method, ITypeSymbol? expressionReceiverType)
        => method.ReceiverType is INamedTypeSymbol { TypeKind: not TypeKind.Error } receiverType
            ? receiverType
            : expressionReceiverType as INamedTypeSymbol;

    private static string ResultHandlerFullName(
        string eventFullName,
        string serverContextFullName,
        string resultFullName,
        bool hasServerContextParameter,
        bool isAsyncLocal,
        bool hasCancellationToken)
    {
        if (isAsyncLocal)
        {
            var token = TypeNames.GlobalCancellationToken;
            var result = TypeNames.GlobalValueTask + "<" + resultFullName + ">";
            if (hasServerContextParameter && hasCancellationToken)
            {
                return $"{TypeNames.GlobalFunc}<{eventFullName}, {serverContextFullName}, {token}, {result}>";
            }

            if (hasServerContextParameter)
            {
                return $"{TypeNames.GlobalFunc}<{eventFullName}, {serverContextFullName}, {result}>";
            }

            return hasCancellationToken
                ? $"{TypeNames.GlobalFunc}<{eventFullName}, {token}, {result}>"
                : $"{TypeNames.GlobalFunc}<{eventFullName}, {result}>";
        }

        if (!hasServerContextParameter)
        {
            return $"{TypeNames.GlobalFunc}<{eventFullName}, {resultFullName}>";
        }

        return $"{TypeNames.GlobalFunc}<{eventFullName}, {serverContextFullName}, {resultFullName}>";
    }

    private static string ServerContextFullName(
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken,
        string? generatedRemoteServerContextTypeFullName)
    {
        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol receiverType)
        {
            return generatedRemoteServerContextTypeFullName ?? TypeNames.GlobalHookContext;
        }

        var original = receiverType.OriginalDefinition.ToDisplayString();
        return original is DotBoxDGenerationNames.TypeNames.HookPipelineWithContextOriginal
            or DotBoxDGenerationNames.TypeNames.HookStageWithContextOriginal
            or DotBoxDGenerationNames.TypeNames.RemoteHookPipelineWithContextOriginal
            or DotBoxDGenerationNames.TypeNames.RemoteHookStageWithContextOriginal
            ? receiverType.TypeArguments[receiverType.TypeArguments.Length - 1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : TypeNames.GlobalHookContext;
    }
}
