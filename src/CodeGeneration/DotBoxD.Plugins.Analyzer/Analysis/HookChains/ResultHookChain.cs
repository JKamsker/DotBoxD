using DotBoxD.Plugins.Analyzer.Analysis.HookResults;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>Lowers Register/RegisterLocal chains to verified result-hook packages.</summary>
internal static class ResultHookChain
{
    public static HookChainResult? Build(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken,
        IReadOnlyList<HookChainStage> stages,
        INamedTypeSymbol contextType,
        EquatableArray<EventPropertyModel> eventProperties,
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
        string? terminalContextParam,
        bool terminalHasCancellationToken,
        bool isLocal,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
    {
        // A Select before the result terminal would re-type the flowing element; v1 supports only Where filters.
        foreach (var stage in stages)
        {
            if (stage.IsSelect)
            {
                throw new NotSupportedException();
            }
        }

        if (!TryResolveHook(contextType, out var hookName, out var resultType) ||
            !HookResultModelFactory.CanSatisfyHookResult(resultType, model.Compilation, cancellationToken))
        {
            throw new NotSupportedException();
        }

        if (terminalHasCancellationToken && !isLocal)
        {
            throw new NotSupportedException();
        }

        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var shouldHandle = HookChainStageLowerer.CreateShouldHandle(
            stages, eventProperties, model, cancellationToken, capabilities, effects);

        DotBoxDStatementBodyModel handleBody;
        string handleReturnType;
        var terminalServerContextType = terminalContextParam is null
            ? null
            : LambdaParameterType(terminalLambda, terminalContextParam, model, cancellationToken);

        if (isLocal)
        {
            ResultHookLocalHandlerValidator.EnsureReturnsHookResult(
                terminalLambda,
                resultType,
                model,
                cancellationToken);

            // RegisterLocal verifies the filter, then asks the native delegate for the result.
            handleBody = DotBoxDHandleBodyModelFactory.ReturnUnit();
            handleReturnType = TypeNames.GlobalSandboxType + ".Unit";
        }
        else
        {
            handleBody = LowerResultHandle(
                terminalLambda,
                terminalElementParam,
                terminalContextParam,
                terminalServerContextType,
                resultType,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
            handleReturnType = SandboxTypeSourceEmitter.TryEmit(resultType)
                ?? throw new NotSupportedException();
        }

        var (indexPredicates, indexCoversPredicate) = HookChainIndexPredicateExtractor.Extract(
            stages, eventProperties, model, cancellationToken);

        var chainId = HookChainIdentity.Compute(invocation);
        var kernelName = "HookChain_" + chainId;
        var kernelModel = new PluginKernelModel(
            PluginId: "chain-" + chainId,
            Namespace: HookChainIdentity.Namespace(invocation),
            KernelName: kernelName,
            PackageName: kernelName + DotBoxDGenerationNames.PluginPackageSuffix,
            EventName: hookName,
            EventParameterName: DotBoxDGenerationNames.DefaultEventParameterName,
            ContextParameterName: terminalContextParam ?? DotBoxDGenerationNames.DefaultContextParameterName,
            HandleEventParameterName: terminalElementParam,
            HandleContextParameterName: terminalContextParam ?? DotBoxDGenerationNames.DefaultContextParameterName,
            EventProperties: eventProperties,
            LiveSettings: default,
            ShouldHandle: shouldHandle,
            HandleBody: handleBody,
            HandleReturnTypeSource: handleReturnType,
            ManifestEffects: DotBoxDManifestEffectModel.CreateLocalCallback(shouldHandle, handleBody, effects),
            RequiredCapabilities: EquatableArray<string>.FromOwned([.. capabilities]),
            IndexPredicates: indexPredicates,
            IndexCoversPredicate: indexCoversPredicate)
        {
            ResultType = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ResultLocalTerminal = isLocal,
        };

        return new HookChainResult(kernelModel, Interception(
            invocation,
            receiver,
            model,
            kernelModel,
            contextType,
            resultType,
            isLocal,
            terminalHasCancellationToken,
            terminalContextParam,
            terminalServerContextType,
            receiverIsStage: false,
            generatedRemoteKind,
            cancellationToken));
    }

    public static bool IsResultTerminal(string terminalMethod)
        => string.Equals(terminalMethod, "Register", StringComparison.Ordinal)
            || string.Equals(terminalMethod, "RegisterLocal", StringComparison.Ordinal);

    private static DotBoxDStatementBodyModel LowerResultHandle(
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
        string? terminalContextParam,
        ITypeSymbol? terminalContextType,
        INamedTypeSymbol resultType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        if (terminalLambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        // Fluent result builders are generated later, so unresolved builder-chain bodies fall back to their seed.
        var bodyType = model.GetTypeInfo(body, cancellationToken).ConvertedType
            ?? model.GetTypeInfo(body, cancellationToken).Type;
        if (bodyType is { TypeKind: not TypeKind.Error })
        {
            if (!SymbolEqualityComparer.Default.Equals(bodyType, resultType))
            {
                throw new NotSupportedException();
            }
        }
        else if (body is not InvocationExpressionSyntax builderChain ||
            !SymbolEqualityComparer.Default.Equals(
                DotBoxDResultBuilderExpressionLowerer.ResolveSeedResultType(builderChain, model, cancellationToken),
                resultType))
        {
            throw new NotSupportedException();
        }

        var context = new DotBoxDExpressionLoweringContext(
            terminalElementParam, eventProperties, default, model, cancellationToken,
            serverContextParameterName: terminalContextParam,
            serverContextType: terminalContextType,
            capabilities: capabilities, effects: effects);
        var lowered = DotBoxDExpressionModelFactory.Create(body, context);
        if (!string.Equals(lowered.Type, DotBoxDGenerationNames.ManifestTypes.Record, StringComparison.Ordinal))
        {
            throw new NotSupportedException();
        }

        return DotBoxDHandleBodyModelFactory.ReturnExpression(lowered);
    }

    private static HookChainInterception? Interception(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver,
        SemanticModel model,
        PluginKernelModel kernelModel,
        INamedTypeSymbol contextType,
        INamedTypeSymbol resultType,
        bool isLocal,
        bool isAsyncLocal,
        string? terminalContextParam,
        ITypeSymbol? terminalContextType,
        bool receiverIsStage,
        GeneratedRemoteHookChainKind? generatedRemoteKind,
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
        var serverContextFullName = terminalContextType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ?? TypeNames.GlobalHookContext;
        var handlerFullName = terminalContextParam is null
            ? $"{TypeNames.GlobalFunc}<{contextFullName}, {resultFullName}>"
            : isLocal && isAsyncLocal
            ? $"{TypeNames.GlobalFunc}<" +
                $"{contextFullName}, {serverContextFullName}, {TypeNames.GlobalCancellationToken}, " +
                $"{TypeNames.GlobalValueTask}<{resultFullName}>>"
            : $"{TypeNames.GlobalFunc}<{contextFullName}, {serverContextFullName}, {resultFullName}>";

        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol receiverType ||
            receiverType.TypeKind == TypeKind.Error)
        {
            return generatedRemoteKind is null
                ? null
                : GeneratedRemoteHookChainFallback.CreateResultInterception(
                    location.GetInterceptsLocationAttributeSyntax(),
                    contextFullName,
                    receiverIsStage,
                    resultFullName,
                    packageFullName,
                    isLocal ? HookChainInterceptorInstallKind.LocalResultChain : HookChainInterceptorInstallKind.ResultChain,
                    generatedRemoteKind.Value,
                    isAsyncLocal);
        }

        var receiverFullName = receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new HookChainInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            receiverFullName,
            handlerFullName,
            receiverFullName,
            packageFullName,
            isLocal ? HookChainInterceptorInstallKind.LocalResultChain : HookChainInterceptorInstallKind.ResultChain,
            ResultTypeFullName: resultFullName,
            IsAsyncLocalResult: isAsyncLocal);
    }

    private static bool TryResolveHook(
        INamedTypeSymbol contextType,
        out string hookName,
        out INamedTypeSymbol resultType)
    {
        hookName = string.Empty;
        resultType = null!;
        foreach (var attribute in contextType.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), DotBoxDMetadataNames.HookAttribute, StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value is string declaredName &&
                !string.IsNullOrWhiteSpace(declaredName) &&
                attribute.ConstructorArguments[1].Value is INamedTypeSymbol declaredResult)
            {
                hookName = declaredName;
                resultType = declaredResult;
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? LambdaParameterType(
        LambdaExpressionSyntax lambda,
        string parameterName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda is not ParenthesizedLambdaExpressionSyntax parenthesized)
        {
            return null;
        }

        foreach (var parameter in parenthesized.ParameterList.Parameters)
        {
            if (string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
            {
                return model.GetDeclaredSymbol(parameter, cancellationToken)?.Type;
            }
        }

        return null;
    }
}
