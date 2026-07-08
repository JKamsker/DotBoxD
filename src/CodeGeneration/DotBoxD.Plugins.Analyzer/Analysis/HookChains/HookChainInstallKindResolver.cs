namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainInstallKindResolver
{
    public static HookChainInterceptorInstallKind? Resolve(
        PipelineCallRole? terminalRole,
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => terminalRole switch
        {
            PipelineCallRole.Run => HookChainInterceptorInstallKind.GeneratedChain,
            PipelineCallRole.RunLocal => RunLocalInstallKind(receiverKind, generatedRemoteKind),
            PipelineCallRole.Register => RegisterInstallKind(receiverKind, generatedRemoteKind),
            PipelineCallRole.RegisterLocal => RegisterLocalInstallKind(receiverKind, generatedRemoteKind),
            _ => null,
        };

    private static HookChainInterceptorInstallKind? RunLocalInstallKind(
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => receiverKind == HookChainReceiverKind.Remote || generatedRemoteKind is not null
            ? HookChainInterceptorInstallKind.LocalCallback
            : null;

    private static HookChainInterceptorInstallKind? RegisterInstallKind(
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => IsResultChainReceiver(receiverKind, generatedRemoteKind)
            ? HookChainInterceptorInstallKind.ResultChain
            : null;

    private static HookChainInterceptorInstallKind? RegisterLocalInstallKind(
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => IsResultChainReceiver(receiverKind, generatedRemoteKind)
            ? HookChainInterceptorInstallKind.LocalResultChain
            : null;

    private static bool IsResultChainReceiver(
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => receiverKind is HookChainReceiverKind.Local or HookChainReceiverKind.Remote ||
           generatedRemoteKind == GeneratedRemoteHookChainKind.Hook;
}
