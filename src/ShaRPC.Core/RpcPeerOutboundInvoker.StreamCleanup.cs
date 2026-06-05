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
            if (stream is null)
            {
                continue;
            }

            await stream.DisposeSourceBestEffortAsync("Outbound stream source cleanup failed")
                .ConfigureAwait(false);
        }
    }
}
