using System.Buffers.Binary;
using System.Text.Json;

namespace DotBoxD.Pushdown.Services;

internal static class PluginDebugBridgeProtocol
{
    private const int JsonHeadroom = 4096;

    public static int WrappedEnvelopeLimit(int envelopeLimit)
    {
        var frameLimit = (((long)envelopeLimit + 2) / 3 * 4) + JsonHeadroom;
        if (frameLimit > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(envelopeLimit),
                envelopeLimit,
                "The bridge envelope limit is too large to wrap in a local frame.");
        }

        return (int)frameLimit;
    }

    public static async ValueTask<JsonDocument?> ReadAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxBytes)
        {
            throw new InvalidDataException($"Bridge frame length {length} exceeds the {maxBytes}-byte limit.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(payload, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        object message,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        if (payload.Length > maxBytes)
        {
            throw new InvalidDataException($"Bridge frame exceeds the {maxBytes}-byte limit.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> ReadExactlyOrEndAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var read = await stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return false;
        }

        await stream.ReadExactlyAsync(destination[read..], cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Connection teardown.
        }
    }
}
