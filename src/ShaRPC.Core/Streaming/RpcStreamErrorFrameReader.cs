using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Streaming;

internal static class RpcStreamErrorFrameReader
{
    public static bool TryRead(
        Payload frame,
        ISerializer serializer,
        out int streamId,
        out RpcResponse response)
    {
        response = default;
        if (!MessageFramer.TryReadFrame(
                frame.Memory,
                out streamId,
                out var type,
                out var envelope,
                out var payload) ||
            !payload.IsEmpty ||
            streamId == 0 ||
            type != MessageType.StreamError)
        {
            return false;
        }

        try
        {
            response = serializer.Deserialize<RpcResponse>(envelope);
        }
        catch
        {
            return false;
        }

        if (response.IsSuccess ||
            response.MessageId != streamId ||
            response.Stream is not null)
        {
            response = default;
            return false;
        }

        return true;
    }
}
