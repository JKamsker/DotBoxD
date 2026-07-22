using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

internal enum StreamFrameReadProgressFormat
{
    WholeFrame,
    Body,
}

internal static class StreamFrameReadOperations
{
    public const int LengthPrefixSize = sizeof(int);

    public static bool IsTimeoutCancellation(FrameReadTimeoutSource? readTimeout) =>
        readTimeout is not null && readTimeout.IsCurrentOwnerTimeoutCancellation();

    public static RpcFrame HandleEndOfStream(
        Payload? payload,
        int remaining,
        StreamFrameReadProgressFormat progressFormat)
    {
        var targetLength = payload is null
            ? LengthPrefixSize
            : payload.Length - LengthPrefixSize;
        var bytesRead = targetLength - remaining;
        if (payload is null)
        {
            if (bytesRead == 0)
            {
                return new RpcFrame(Payload.Empty);
            }

            throw new InvalidDataException(
                $"Connection closed after {bytesRead} of {LengthPrefixSize} frame length bytes.");
        }

        var completed = progressFormat == StreamFrameReadProgressFormat.WholeFrame
            ? bytesRead + LengthPrefixSize
            : bytesRead;
        var expected = progressFormat == StreamFrameReadProgressFormat.WholeFrame
            ? targetLength + LengthPrefixSize
            : targetLength;
        throw new InvalidDataException(
            $"Connection closed after {completed} of {expected} frame bytes.");
    }
}
