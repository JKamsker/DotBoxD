using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// Lowers a result-returning hook chain — <c>On&lt;TContext&gt;().Where*(lambda).Register(lambda, priority)</c>
/// or <c>.RegisterLocal(lambda, priority)</c> — to a <see cref="PluginKernelModel"/>. The result type is taken
/// from <c>[Hook]</c> on the context. A <c>Register</c> chain reuses the projection package shape: the
/// <c>Where</c> filter lowers to <c>ShouldHandle</c> and the result-producing lambda body lowers to a
/// <c>Handle</c> that returns the result record (so the validator's projection-Handle path applies); the host
/// installs it via <c>UseGeneratedResultChain</c> and decodes the result rather than pushing it. A
/// <c>RegisterLocal</c> chain lowers only the filter (whole-event shape, Unit Handle); the plugin delegate
/// produces the result. Any unsupported shape throws <see cref="NotSupportedException"/> so the chain fails
/// safe (no package, DBXK113).
/// </summary>
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
        bool isLocal)
    {
        // A Select before the result terminal would re-type the flowing element; v1 supports only Where filters.
        foreach (var stage in stages)
        {
            if (stage.IsSelect)
            {
                throw new NotSupportedException();
            }
        }

        if (!TryResolveHook(contextType, out var resultType) ||
            !IsHookResultType(resultType))
        {
            throw new NotSupportedException();
        }

        // The handler's inferred TResult must be the context's associated result type.
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            method.TypeArguments.Length != 1 ||
            !SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], resultType))
        {
            throw new NotSupportedException();
        }

        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var shouldHandle = HookChainStageLowerer.CreateShouldHandle(
            stages, eventProperties, model, cancellationToken, capabilities, effects);

        DotBoxDStatementBodyModel handleBody;
        string handleReturnType;
        string? projectedType;
        if (isLocal)
        {
            // RegisterLocal: only the filter is verified IR; the Handle returns Unit and the plugin delegate
            // produces the result. Whole-event shape (LocalTerminal, no ProjectedType).
            handleBody = DotBoxDHandleBodyModelFactory.ReturnUnit();
            handleReturnType = TypeNames.GlobalSandboxType + ".Unit";
            projectedType = null;
        }
        else
        {
            handleBody = LowerResultHandle(terminalLambda, terminalElementParam, resultType, eventProperties, model, cancellationToken, capabilities, effects);
            handleReturnType = SandboxTypeSourceEmitter.TryEmit(resultType)
                ?? throw new NotSupportedException();
            projectedType = DotBoxDGenerationNames.ManifestTypes.Record;
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
            EventName: EventTypeName.Qualified(contextType),
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
            LocalTerminal = true,
            ProjectedType = projectedType,
        };

        return new HookChainResult(kernelModel, Interception(invocation, receiver, model, kernelModel, method, resultType, isLocal, cancellationToken));
    }

    public static bool IsResultTerminal(string terminalMethod)
        => string.Equals(terminalMethod, "Register", StringComparison.Ordinal)
            || string.Equals(terminalMethod, "RegisterLocal", StringComparison.Ordinal);

    private static DotBoxDStatementBodyModel LowerResultHandle(
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
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

        // The lowered body must construct the associated result record exactly (record.new of the result type).
        var bodyType = model.GetTypeInfo(body, cancellationToken).ConvertedType
            ?? model.GetTypeInfo(body, cancellationToken).Type;
        if (!SymbolEqualityComparer.Default.Equals(bodyType, resultType))
        {
            throw new NotSupportedException();
        }

        var context = new DotBoxDExpressionLoweringContext(
            terminalElementParam, eventProperties, default, model, cancellationToken,
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
        IMethodSymbol method,
        INamedTypeSymbol resultType,
        bool isLocal,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null ||
            method.Parameters.Length < 1 ||
            model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol receiverType)
        {
            return null;
        }

        var packageFullName = string.IsNullOrEmpty(kernelModel.Namespace)
            ? TypeNames.GlobalPrefix + kernelModel.PackageName
            : TypeNames.GlobalPrefix + kernelModel.Namespace + "." + kernelModel.PackageName;

        return new HookChainInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            packageFullName,
            isLocal ? HookChainInterceptorInstallKind.LocalResultChain : HookChainInterceptorInstallKind.ResultChain,
            ResultTypeFullName: resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static bool TryResolveHook(INamedTypeSymbol contextType, out INamedTypeSymbol resultType)
    {
        resultType = null!;
        foreach (var attribute in contextType.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), DotBoxDGenerationNames.Metadata.HookAttribute, StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[1].Value is INamedTypeSymbol declaredResult)
            {
                resultType = declaredResult;
                return true;
            }
        }

        return false;
    }

    private static bool IsHookResultType(INamedTypeSymbol resultType)
    {
        if (!resultType.IsValueType)
        {
            return false;
        }

        foreach (var attribute in resultType.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), DotBoxDGenerationNames.Metadata.HookResultAttribute, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
