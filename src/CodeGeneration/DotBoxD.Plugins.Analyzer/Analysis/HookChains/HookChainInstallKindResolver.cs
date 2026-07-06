namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainInstallKindResolver
{
    private static readonly Dictionary<string, InstallKindResolver> InstallKindResolvers =
        new(StringComparer.Ordinal)
        {
            [HookChainModelFactory.RunMethod] = static (_, _) => HookChainInterceptorInstallKind.GeneratedChain,
            [HookChainModelFactory.RunLocalMethod] = RunLocalInstallKind,
            [HookChainModelFactory.RegisterMethod] = RegisterInstallKind,
            [HookChainModelFactory.RegisterLocalMethod] = RegisterLocalInstallKind,
        };

    private delegate HookChainInterceptorInstallKind? InstallKindResolver(
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind);

    public static HookChainInterceptorInstallKind? Resolve(
        string terminalMethod,
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => InstallKindResolvers.TryGetValue(terminalMethod, out var resolver)
            ? resolver(receiverKind, generatedRemoteKind)
            : null;

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
