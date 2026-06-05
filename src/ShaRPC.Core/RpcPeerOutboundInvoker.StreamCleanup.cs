using ShaRPC.Core.Streaming;

namespace ShaRPC.Core;

internal sealed partial class RpcPeerOutboundInvoker
{
    private static async ValueTask DisposeStreamSourcesBestEffortAsync(RpcStreamAttachment[]? streams)
    {
        if (streams is null)
        {
            return;
        }

        foreach (var stream in streams)
        {
            try
            {
                await stream.DisposeSourceAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RpcDiagnostics.Report("Outbound stream source cleanup failed", ex);
            }
        }
    }
}
