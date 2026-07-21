using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

internal static class RpcFramePayloadAdapter
{
    public static ValueTask<Payload> DetachAsync(ValueTask<RpcFrame> pending)
    {
        if (pending.IsCompletedSuccessfully)
        {
            var frame = pending.Result;
            return new ValueTask<Payload>(frame.DetachPayload());
        }

        return AwaitAsync(pending);
    }

    private static async ValueTask<Payload> AwaitAsync(ValueTask<RpcFrame> pending)
    {
        var frame = await pending.ConfigureAwait(false);
        return frame.DetachPayload();
    }
}
