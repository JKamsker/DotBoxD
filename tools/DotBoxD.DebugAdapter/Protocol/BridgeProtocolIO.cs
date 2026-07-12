using System.Buffers.Binary;
using System.Text.Json;

namespace DotBoxD.DebugAdapter;

internal static class BridgeProtocolIO
{
    private const int RemoteEnvelopeLimit = 1024 * 1024;
    private const int JsonHeadroom = 4096;
    private const int MaxFrameLength = ((RemoteEnvelopeLimit + 2) / 3 * 4) + JsonHeadroom;

    public static async ValueTask WriteAsync(
        Stream stream,
        object message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, DapJson.Options);
        if (payload.Length > MaxFrameLength)
        {
            throw new InvalidDataException("Bridge frame is outside the adapter limit.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<JsonDocument?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        var first = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        if (first == 0)
        {
            return null;
        }

        await stream.ReadExactlyAsync(header.AsMemory(first), cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameLength)
        {
            throw new InvalidDataException("Bridge frame is outside the adapter limit.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(payload);
    }
}
