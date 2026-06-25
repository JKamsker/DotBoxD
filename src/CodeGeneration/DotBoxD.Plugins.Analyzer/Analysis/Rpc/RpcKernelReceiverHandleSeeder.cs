namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using static DotBoxDRpcJsonLowerer;

internal static class RpcKernelReceiverHandleSeeder
{
    public const string ReceiverIdParameter = "__receiverId";

    public static bool TrySeed(
        DotBoxDRpcJsonLowerer lowerer,
        RpcServerExtensionGraft? graft)
    {
        if (graft is not { InjectsReceiverId: true })
        {
            return false;
        }

        foreach (var field in graft.ReceiverHandleFields)
        {
            lowerer.AddServiceHandleLocal(field, Var(ReceiverIdParameter));
        }

        return true;
    }
}
