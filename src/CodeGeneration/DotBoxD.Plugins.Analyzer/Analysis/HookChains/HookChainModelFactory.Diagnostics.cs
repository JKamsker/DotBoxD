using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static HookChainCreateResult? NotLoweredDiagnostic(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        if (HasDiscardedStageReceiver(invocation, model, cancellationToken))
        {
            return null;
        }

        if (TryRemoteRunLocalLocation(invocation, model, cancellationToken, out var location))
        {
            return new HookChainCreateResult(
                null,
                new HookChainNotLoweredDiagnostic(location, Detail: detail ?? string.Empty));
        }

        if (TryResultChainLocation(invocation, model, cancellationToken, out var resultLocation, out var isLocalTerminal))
        {
            return new HookChainCreateResult(
                null,
                new HookChainNotLoweredDiagnostic(
                    resultLocation,
                    HookChainNotLoweredKind.ResultChain,
                    LocalResultTerminal: isLocalTerminal,
                    Detail: detail ?? string.Empty));
        }

        if (TryRunChainLocation(invocation, model, cancellationToken, out var runLocation))
        {
            return new HookChainCreateResult(
                null,
                new HookChainNotLoweredDiagnostic(
                    runLocation,
                    HookChainNotLoweredKind.RunChain,
                    Detail: detail ?? string.Empty));
        }

        return null;
    }

    private static bool HasDiscardedStageReceiver(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
        => invocation.Expression is MemberAccessExpressionSyntax terminalAccess &&
           HasPriorDiscardedStageOnReceiver(terminalAccess.Expression, invocation, model, cancellationToken);

    // True when the call site is a Register/RegisterLocal terminal on a known hook pipeline - the surface whose
    // native terminal throws when the generator does not intercept it.
    private static bool TryResultChainLocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location,
        out bool isLocalTerminal)
    {
        location = default;
        isLocalTerminal = false;
        var terminalRole = RoleOf(invocation, model, cancellationToken);
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            terminalRole is not (PipelineCallRole.Register or PipelineCallRole.RegisterLocal))
        {
            return false;
        }

        var receiverKind = ReceiverKind(model, terminalAccess.Expression, cancellationToken);
        if (receiverKind is not (HookChainReceiverKind.Local or HookChainReceiverKind.Remote))
        {
            var stages = new List<HookChainStage>();
            var seed = WalkToSeed(terminalAccess.Expression, stages, model, cancellationToken);
            if (seed is null ||
                GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken)?.Kind !=
                GeneratedRemoteHookChainKind.Hook)
            {
                return false;
            }
        }

        isLocalTerminal = terminalRole == PipelineCallRole.RegisterLocal;
        location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
        return true;
    }

    // True when the call site is a remote RunLocal terminal: RunLocal whose receiver's static type is one of the
    // remote hook/subscription stage/pipeline types. Those (and only those) throw NotSupportedException when the
    // generator does not intercept them, so a remote RunLocal that produced no package will throw at runtime.
    private static bool TryRemoteRunLocalLocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location)
    {
        location = default;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            RoleOf(invocation, model, cancellationToken) != PipelineCallRole.RunLocal)
        {
            return false;
        }

        if (ReceiverKind(model, terminalAccess.Expression, cancellationToken) == HookChainReceiverKind.Remote)
        {
            location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
            return true;
        }

        var stages = new List<HookChainStage>();
        var seed = WalkToSeed(terminalAccess.Expression, stages, model, cancellationToken);
        if (seed is null ||
            GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken) is null)
        {
            return false;
        }

        location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
        return true;
    }

    private static bool TryRunChainLocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location)
    {
        location = default;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            RoleOf(invocation, model, cancellationToken) != PipelineCallRole.Run)
        {
            return false;
        }

        if (ReceiverKind(model, terminalAccess.Expression, cancellationToken) is HookChainReceiverKind.Local
            or HookChainReceiverKind.Remote)
        {
            location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
            return true;
        }

        var stages = new List<HookChainStage>();
        var seed = WalkToSeed(terminalAccess.Expression, stages, model, cancellationToken);
        if (seed is null ||
            GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken) is null)
        {
            return false;
        }

        location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
        return true;
    }

    private static void ValidateEventType(
        INamedTypeSymbol eventType,
        InvocationExpressionSyntax seed,
        CancellationToken cancellationToken)
    {
        if (!IsFileLocal(eventType, cancellationToken))
        {
            return;
        }

        var location = EventTypeLocation(seed) ?? PluginDiagnosticLocation.From(seed.GetLocation());
        throw new UnsupportedHookChainEventTypeException(
            "File-local event type '" + eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "' cannot be used in a generated hook chain because interceptor source is emitted in a separate " +
            "generated file; use a source-nameable event type instead.",
            location);
    }

    private static void ValidateServerContextType(
        InvocationExpressionSyntax seed,
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainTarget? generatedRemoteTarget,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (ServerContextType(seed, generatedRemoteTarget, model, cancellationToken) is not { } serverContextType ||
            FindFileLocalType(serverContextType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) is not { } fileLocalType)
        {
            return;
        }

        var location = ServerContextTypeLocation(seed) ?? PluginDiagnosticLocation.From(seed.GetLocation());
        var chainKind = generatedRemoteTarget is not null || receiverKind == HookChainReceiverKind.Remote
            ? "Remote hook-chain"
            : "Hook chain";
        throw new HookChainUnsupportedDiagnosticException(
            new PluginKernelDiagnostic(
                chainKind +
                " server context type '" +
                fileLocalType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                "' is file-local; generated hook-chain sources cannot name file-local types. " +
                "Use a named server context type that is visible to generated code, or use HookContext directly.",
                location));
    }

    private static ITypeSymbol? ServerContextType(
        InvocationExpressionSyntax seed,
        GeneratedRemoteHookChainTarget? generatedRemoteTarget,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (seed.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax onName } &&
            onName.TypeArgumentList.Arguments.Count >= 2)
        {
            return model.GetTypeInfo(onName.TypeArgumentList.Arguments[1], cancellationToken).Type;
        }

        return generatedRemoteTarget is { } target
            ? GeneratedRemoteHookChainFallback.ServerContextType(model, seed, target, cancellationToken)
            : null;
    }

    private static bool IsFileLocal(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is BaseTypeDeclarationSyntax declaration &&
                HasFileModifier(declaration.Modifiers))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFileModifier(SyntaxTokenList modifiers)
    {
        foreach (var modifier in modifiers)
        {
            if (modifier.IsKind(SyntaxKind.FileKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static PluginDiagnosticLocation? EventTypeLocation(InvocationExpressionSyntax seed)
    {
        if (seed.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax onName } &&
            onName.TypeArgumentList.Arguments.Count > 0)
        {
            return PluginDiagnosticLocation.From(onName.TypeArgumentList.Arguments[0].GetLocation());
        }

        return null;
    }
}

internal sealed class UnsupportedHookChainEventTypeException : Exception
{
    public UnsupportedHookChainEventTypeException(string message, PluginDiagnosticLocation location)
        : base(message)
        => Location = location;

    public PluginDiagnosticLocation Location { get; }
}
