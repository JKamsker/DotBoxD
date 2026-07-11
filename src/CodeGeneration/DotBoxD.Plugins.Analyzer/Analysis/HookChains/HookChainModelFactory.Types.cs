using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private readonly record struct HookChainResolution(
        HookChainReceiverKind? ReceiverKind,
        GeneratedRemoteHookChainTarget? GeneratedRemoteTarget,
        GeneratedRemoteHookChainKind? GeneratedRemoteKind,
        string? GeneratedRemoteServerContextTypeFullName,
        HookChainInterceptorInstallKind? InstallKind);

    private readonly record struct HookChainTerminal(
        LambdaExpressionSyntax Lambda,
        string? ElementParam,
        string? ContextParam,
        bool IsAsyncLocal,
        bool HasCancellationToken)
    {
        public static HookChainTerminal From(LambdaExpressionSyntax lambda, ResultLocalTerminalShape shape)
            => new(lambda, shape.ElementParam, shape.ContextParam, shape.AsyncLocal, shape.HasCancellationToken);

        public static HookChainTerminal From(
            LambdaExpressionSyntax lambda,
            (string? ElementParam, string? ContextParam) parameters)
            => new(lambda, parameters.ElementParam, parameters.ContextParam, IsAsyncLocal: false, HasCancellationToken: false);
    }

    private readonly record struct PreparedHookChain(
        MemberAccessExpressionSyntax TerminalAccess,
        InvocationExpressionSyntax Seed,
        List<HookChainStage> Stages,
        HookChainInterceptorInstallKind InstallKind,
        GeneratedRemoteHookChainKind? GeneratedRemoteKind,
        string? GeneratedRemoteServerContextTypeFullName,
        LambdaExpressionSyntax TerminalLambda,
        string TerminalElementParam,
        string? TerminalContextParam,
        bool TerminalIsAsyncLocal,
        bool TerminalHasCancellationToken);

    private readonly record struct HookChainEventShape(
        INamedTypeSymbol EventType,
        EquatableArray<EventPropertyModel> EventProperties);

    private sealed record HookChainCollectors(
        SortedSet<string> Capabilities,
        SortedSet<string> Effects)
    {
        public static HookChainCollectors Create()
            => new(
                new SortedSet<string>(StringComparer.Ordinal),
                new SortedSet<string>(StringComparer.Ordinal));
    }

    private sealed record SendHookLowering(
        DotBoxDStatementBodyModel ShouldHandle,
        DotBoxDStatementBodyModel HandleBody,
        HookChainProjection? LocalCallbackProjection,
        ITypeSymbol? ProjectedTypeSymbol,
        string HandleReturnType,
        string? LocalDecoderSource);
}
