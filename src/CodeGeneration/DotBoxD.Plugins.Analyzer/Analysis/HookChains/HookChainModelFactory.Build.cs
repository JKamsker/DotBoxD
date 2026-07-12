using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static HookChainResult? BuildResultHookChain(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        PreparedHookChain prepared,
        HookChainEventShape eventShape)
        => ResultHookChain.Build(
            invocation,
            prepared.TerminalAccess.Expression,
            model,
            cancellationToken,
            prepared.Stages,
            eventShape.EventType,
            eventShape.EventProperties,
            prepared.TerminalLambda,
            prepared.TerminalElementParam,
            prepared.TerminalContextParam,
            prepared.InstallKind == HookChainInterceptorInstallKind.LocalResultChain,
            prepared.TerminalIsAsyncLocal,
            prepared.TerminalHasCancellationToken,
            prepared.GeneratedRemoteKind,
            prepared.GeneratedRemoteServerContextTypeFullName);

    private static HookChainResult BuildSendHookChain(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        PreparedHookChain prepared,
        HookChainEventShape eventShape)
    {
        var collectors = HookChainCollectors.Create();
        var lowered = LowerSendHookBody(model, cancellationToken, prepared, eventShape, collectors);
        var modelResult = BuildSendHookModel(
            invocation,
            model,
            cancellationToken,
            prepared,
            eventShape,
            lowered,
            collectors);
        modelResult = HookChainDebugSourceFactory.ApplyToSend(
            modelResult,
            invocation,
            prepared.Stages,
            prepared.TerminalLambda,
            prepared.InstallKind == HookChainInterceptorInstallKind.LocalCallback,
            model,
            cancellationToken);
        var interception = BuildSendHookInterception(
            invocation,
            model,
            cancellationToken,
            prepared,
            eventShape,
            lowered,
            modelResult);
        var stageIrModels = HookChainStageIrFactory.Create(
            invocation,
            prepared.Stages,
            eventShape.EventType,
            prepared.GeneratedRemoteKind,
            prepared.GeneratedRemoteServerContextTypeFullName,
            model,
            cancellationToken);
        return new HookChainResult(modelResult, interception, stageIrModels);
    }

    private static SendHookLowering LowerSendHookBody(
        SemanticModel model,
        CancellationToken cancellationToken,
        PreparedHookChain prepared,
        HookChainEventShape eventShape,
        HookChainCollectors collectors)
    {
        var terminalContextType = TerminalContextType(prepared, model, cancellationToken);
        var shouldHandle = HookChainStageLowerer.CreateShouldHandle(
            prepared.Stages,
            eventShape.EventProperties,
            model,
            cancellationToken,
            collectors.Capabilities,
            collectors.Effects);
        var projection = LocalCallbackProjection(prepared, eventShape, model, cancellationToken, collectors);
        var handleBody = SendHookHandleBody(
            prepared,
            eventShape,
            terminalContextType,
            projection,
            model,
            cancellationToken,
            collectors);
        var projectedTypeSymbol = ProjectedTypeSymbol(prepared.Stages, eventShape.EventType, model, cancellationToken);
        RejectUnsupportedProjectedType(prepared.InstallKind, projectedTypeSymbol, prepared.TerminalAccess.Name);

        return new SendHookLowering(
            shouldHandle,
            handleBody,
            projection,
            projectedTypeSymbol,
            BuildHandleReturnType(prepared.InstallKind, projection, projectedTypeSymbol),
            BuildLocalDecoderSource(prepared.InstallKind, projectedTypeSymbol, model.Compilation));
    }

    private static DotBoxDStatementBodyModel SendHookHandleBody(
        PreparedHookChain prepared,
        HookChainEventShape eventShape,
        ITypeSymbol? terminalContextType,
        HookChainProjection? localCallbackProjection,
        SemanticModel model,
        CancellationToken cancellationToken,
        HookChainCollectors collectors)
        => prepared.InstallKind == HookChainInterceptorInstallKind.LocalCallback
            ? LocalCallbackHandleBody(localCallbackProjection)
            : LowerRunHandle(
                prepared.Stages,
                prepared.TerminalLambda,
                prepared.TerminalElementParam,
                prepared.TerminalContextParam,
                terminalContextType,
                eventShape.EventProperties,
                model,
                cancellationToken,
                collectors.Capabilities,
                collectors.Effects);

    private static PluginKernelModel BuildSendHookModel(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        PreparedHookChain prepared,
        HookChainEventShape eventShape,
        SendHookLowering lowered,
        HookChainCollectors collectors)
    {
        var (indexPredicates, indexCoversPredicate) = HookChainIndexPredicateExtractor.Extract(
            prepared.Stages,
            eventShape.EventProperties,
            model,
            cancellationToken);
        return BuildSendHookModel(
            invocation,
            prepared,
            eventShape,
            lowered,
            collectors,
            indexPredicates,
            indexCoversPredicate);
    }

    private static PluginKernelModel BuildSendHookModel(
        InvocationExpressionSyntax invocation,
        PreparedHookChain prepared,
        HookChainEventShape eventShape,
        SendHookLowering lowered,
        HookChainCollectors collectors,
        EquatableArray<IndexPredicateModel> indexPredicates,
        bool indexCoversPredicate)
    {
        var chainId = HookChainIdentity.Compute(invocation);
        var kernelName = "HookChain_" + chainId;
        return new PluginKernelModel(
            PluginId: "chain-" + chainId,
            Namespace: HookChainIdentity.Namespace(invocation),
            KernelName: kernelName,
            PackageName: kernelName + "PluginPackage",
            GeneratedPackageAttributes: default,
            GeneratedAttributeSource: ExperimentalAttributeSource.FromTypes(
                eventShape.EventType,
                lowered.ProjectedTypeSymbol),
            EventName: EventTypeName.HookOrQualified(eventShape.EventType),
            EventTypeName: EventTypeName.Qualified(eventShape.EventType),
            EventParameterName: DotBoxDGenerationNames.DefaultEventParameterName,
            ContextParameterName: prepared.TerminalContextParam ?? DotBoxDGenerationNames.DefaultContextParameterName,
            HandleEventParameterName: prepared.TerminalElementParam,
            HandleContextParameterName: prepared.TerminalContextParam ?? DotBoxDGenerationNames.DefaultContextParameterName,
            PackageAttributes: default,
            EventProperties: eventShape.EventProperties,
            LiveSettings: default,
            ShouldHandle: lowered.ShouldHandle,
            HandleBody: lowered.HandleBody,
            HandleReturnTypeSource: lowered.HandleReturnType,
            ManifestEffects: ManifestEffects(prepared.InstallKind, lowered.ShouldHandle, lowered.HandleBody, collectors.Effects),
            RequiredCapabilities: EquatableArray<string>.FromOwned([.. collectors.Capabilities]),
            IndexPredicates: indexPredicates,
            IndexCoversPredicate: indexCoversPredicate)
        {
            // Persist the local-terminal nature in the manifest (a host-readable mark) so the runtime knows to
            // push rather than run. Even no-Select RunLocal chains are emitted as an explicit event-record
            // projection, so ordinary Unit-returning Run packages cannot be relabeled into native callbacks.
            LocalTerminal = prepared.InstallKind == HookChainInterceptorInstallKind.LocalCallback,
            ProjectedType = LocalProjectedManifestType(lowered.LocalCallbackProjection, lowered.ProjectedTypeSymbol),
            LocalDecoderSource = lowered.LocalDecoderSource,
            HandleProjectedSlotName = HookChainProjectionSlotResolver.Final(prepared.Stages),
            HandleProjectedSourceSlotName = HookChainProjectionSlotResolver.FinalSource(
                prepared.Stages,
                eventShape.EventProperties),
        };
    }

    private static HookChainInterception BuildSendHookInterception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        PreparedHookChain prepared,
        HookChainEventShape eventShape,
        SendHookLowering lowered,
        PluginKernelModel modelResult)
    {
        var terminalElementTypeFullName = TerminalElementTypeFullName(
            prepared.Stages,
            eventShape.EventProperties,
            eventShape.EventType,
            model,
            cancellationToken);
        var interception = Interception(
            invocation,
            model,
            modelResult,
            prepared.TerminalAccess.Expression,
            eventShape.EventType,
            prepared.Stages,
            terminalElementTypeFullName,
            prepared.GeneratedRemoteKind,
            prepared.InstallKind,
            prepared.GeneratedRemoteServerContextTypeFullName,
            prepared.TerminalContextParam is not null,
            TerminalReturnsVoid(prepared.TerminalLambda, model, cancellationToken),
            lowered.LocalDecoderSource is not null,
            lowered.ProjectedTypeSymbol,
            out var interceptionFailureReason,
            cancellationToken);

        return interception ?? throw new NotSupportedException(InterceptionFailureDetail(interceptionFailureReason));
    }

    private static HookChainProjection? LocalCallbackProjection(
        PreparedHookChain prepared,
        HookChainEventShape eventShape,
        SemanticModel model,
        CancellationToken cancellationToken,
        HookChainCollectors collectors)
        => prepared.InstallKind == HookChainInterceptorInstallKind.LocalCallback
            ? LocalCallbackProjection(
                prepared.Stages,
                eventShape.EventProperties,
                eventShape.EventType,
                model,
                cancellationToken,
                collectors.Capabilities,
                collectors.Effects)
            : null;

    private static ITypeSymbol? TerminalContextType(
        PreparedHookChain prepared,
        SemanticModel model,
        CancellationToken cancellationToken)
        => prepared.TerminalContextParam is null
            ? null
            : LambdaParameterType(prepared.TerminalLambda, prepared.TerminalContextParam, model, cancellationToken);

    private static string BuildHandleReturnType(
        HookChainInterceptorInstallKind installKind,
        HookChainProjection? localCallbackProjection,
        ITypeSymbol? projectedTypeSymbol)
        => installKind == HookChainInterceptorInstallKind.LocalCallback
            ? LocalCallbackHandleReturnType(localCallbackProjection, projectedTypeSymbol)
            : DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".Unit";

    private static string? BuildLocalDecoderSource(
        HookChainInterceptorInstallKind installKind,
        ITypeSymbol? projectedTypeSymbol,
        Compilation compilation)
        => installKind == HookChainInterceptorInstallKind.LocalCallback
            ? BuildLocalDecoderSource(projectedTypeSymbol, compilation)
            : null;

    private static bool IsResultTerminal(HookChainInterceptorInstallKind installKind)
        => installKind is HookChainInterceptorInstallKind.ResultChain
            or HookChainInterceptorInstallKind.LocalResultChain;
}
