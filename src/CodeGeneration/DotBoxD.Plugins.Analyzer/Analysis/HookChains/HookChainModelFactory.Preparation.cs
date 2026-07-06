using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static bool TryPrepareChain(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PreparedHookChain prepared)
    {
        prepared = default;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess)
        {
            return false;
        }

        if (HasPriorDiscardedStageOnReceiver(terminalAccess.Expression, invocation, model, cancellationToken))
        {
            return false;
        }

        var stages = new List<HookChainStage>();
        var seed = WalkToSeed(terminalAccess.Expression, stages, model, cancellationToken);
        if (seed is null)
        {
            return false;
        }

        return TryBuildPreparedChain(
            invocation,
            terminalAccess,
            seed,
            stages,
            model,
            cancellationToken,
            out prepared);
    }

    private static bool TryBuildPreparedChain(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax terminalAccess,
        InvocationExpressionSyntax seed,
        List<HookChainStage> stages,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PreparedHookChain prepared)
    {
        prepared = default;
        var resolution = ResolveChainInstall(terminalAccess, seed, model, cancellationToken);
        if (resolution.InstallKind is null)
        {
            return false;
        }

        RejectUnsupportedServerContextType(
            ServerContextType(model, terminalAccess.Expression, seed, resolution.GeneratedRemoteTarget, cancellationToken),
            seed,
            terminalAccess.Name);
        ValidateServerContextType(seed, resolution.ReceiverKind, resolution.GeneratedRemoteTarget, model, cancellationToken);
        return TryTerminalShape(invocation, resolution.InstallKind.Value, model, cancellationToken, out var terminal) &&
            CompletePreparedChain(terminalAccess, seed, stages, resolution, terminal, out prepared);
    }

    private static bool CompletePreparedChain(
        MemberAccessExpressionSyntax terminalAccess,
        InvocationExpressionSyntax seed,
        List<HookChainStage> stages,
        HookChainResolution resolution,
        HookChainTerminal terminal,
        out PreparedHookChain prepared)
    {
        prepared = new PreparedHookChain(
            terminalAccess,
            seed,
            stages,
            resolution.InstallKind!.Value,
            resolution.GeneratedRemoteKind,
            resolution.GeneratedRemoteServerContextTypeFullName,
            terminal.Lambda,
            terminal.ElementParam!,
            terminal.ContextParam,
            terminal.IsAsyncLocal,
            terminal.HasCancellationToken);
        return true;
    }

    private static HookChainResolution ResolveChainInstall(
        MemberAccessExpressionSyntax terminalAccess,
        InvocationExpressionSyntax seed,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var receiverKind = ReceiverKind(model, terminalAccess.Expression, cancellationToken);
        var generatedRemoteTarget = receiverKind is null
            ? GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken)
            : null;
        var generatedRemoteKind = receiverKind is null ? generatedRemoteTarget?.Kind : null;
        var serverContextTypeFullName = generatedRemoteTarget is { } target
            ? GeneratedRemoteHookChainFallback.ServerContextTypeFullName(model, seed, target, cancellationToken)
            : null;
        var installKind = generatedRemoteTarget is null && receiverKind is null
            ? null
            : HookChainInstallKindResolver.Resolve(
                terminalAccess.Name.Identifier.ValueText,
                receiverKind,
                generatedRemoteKind);

        return new HookChainResolution(
            receiverKind,
            generatedRemoteTarget,
            generatedRemoteKind,
            serverContextTypeFullName,
            installKind);
    }

    private static bool TryTerminalShape(
        InvocationExpressionSyntax invocation,
        HookChainInterceptorInstallKind installKind,
        SemanticModel model,
        CancellationToken cancellationToken,
        out HookChainTerminal terminal)
    {
        terminal = default;
        if (!TryTerminalLambda(invocation, installKind, out var terminalLambda))
        {
            return false;
        }

        terminal = installKind == HookChainInterceptorInstallKind.LocalResultChain
            ? HookChainTerminal.From(terminalLambda, ResultLocalLambdaParameters(invocation, terminalLambda, model, cancellationToken))
            : HookChainTerminal.From(terminalLambda, ResultLocalTerminalShape.From(LambdaParameters(terminalLambda)));
        return terminal.ElementParam is not null;
    }

    private static bool TryTerminalLambda(
        InvocationExpressionSyntax invocation,
        HookChainInterceptorInstallKind installKind,
        out LambdaExpressionSyntax terminalLambda)
        => IsResultTerminal(installKind)
            ? TryLeadingLambda(invocation, out terminalLambda)
            : TryLambda(invocation, out terminalLambda);

    private static bool TryEventShape(
        InvocationExpressionSyntax seed,
        SemanticModel model,
        CancellationToken cancellationToken,
        out HookChainEventShape eventShape)
    {
        eventShape = default;
        if (!GeneratedRemoteHookChainFallback.TryEventType(model, seed, cancellationToken, out var eventType))
        {
            return false;
        }

        ValidateEventType(eventType, seed, cancellationToken);
        var eventProperties = PluginSymbolReader.EventProperties(eventType);
        if (ContainsUnsupported(eventProperties))
        {
            return false;
        }

        eventShape = new HookChainEventShape(eventType, eventProperties);
        return true;
    }
}
