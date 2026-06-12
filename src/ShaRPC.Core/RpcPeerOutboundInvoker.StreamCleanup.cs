using ShaRPC.Core.Client;
using ShaRPC.Core.Streaming;
using System.Runtime.ExceptionServices;

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

    private static async Task<ReceivedResponse> DisposeStreamSourcesAndThrowAsync(
        RpcStreamAttachment[]? streams,
        Exception error)
    {
        await DisposeStreamSourcesBestEffortAsync(streams).ConfigureAwait(false);
        ExceptionDispatchInfo.Capture(error).Throw();
        throw null!;
    }

    private static async Task<ReceivedResponse> CleanupOutboundSetupFailureAsync(
        RpcOutboundStreamSet outboundStreams,
        RpcStreamAttachment[]? streams,
        bool registeredStreams,
        Exception error)
    {
        await outboundStreams.DisposeAsync().ConfigureAwait(false);
        if (!registeredStreams)
        {
            await DisposeStreamSourcesBestEffortAsync(streams).ConfigureAwait(false);
        }

        ExceptionDispatchInfo.Capture(error).Throw();
        throw null!;
    }
}
