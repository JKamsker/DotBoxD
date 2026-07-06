namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainInstallKindResolver
{
    public static HookChainInterceptorInstallKind? Resolve(
        PipelineStepRole? terminalRole,
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => terminalRole switch
        {
            PipelineStepRole.Run => HookChainInterceptorInstallKind.GeneratedChain,
            PipelineStepRole.RunLocal => RunLocalInstallKind(receiverKind, generatedRemoteKind),
            PipelineStepRole.Register => RegisterInstallKind(receiverKind, generatedRemoteKind),
            PipelineStepRole.RegisterLocal => RegisterLocalInstallKind(receiverKind, generatedRemoteKind),
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
