using System.Buffers;
using System.Buffers.Binary;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Protocol;

/// <summary>
/// Handles message framing for the ShaRPC protocol.
/// Format: [4 bytes: Total Length][4 bytes: MessageId][1 byte: MessageType][N bytes: Payload]
/// </summary>
public static class MessageFramer
{
    /// <summary>
    /// Header size: 4 (length) + 4 (messageId) + 1 (type) = 9 bytes
    /// </summary>
    public const int HeaderSize = 9;

    /// <summary>
    /// Maximum message size (16 MB).
    /// </summary>
    public const int MaxMessageSize = 16 * 1024 * 1024;

    /// <summary>
    /// A framed message read from a stream. The caller owns <see cref="Payload"/> and must dispose it.
    /// </summary>
    public readonly record struct FramedMessage(int MessageId, MessageType Type, Payload Payload);

    /// <summary>
    /// Writes a complete frame (header + payload) into the supplied buffer writer.
    /// </summary>
    public static void WriteFrame(IBufferWriter<byte> writer, int messageId, MessageType type, ReadOnlySpan<byte> payload)
    {
        var totalLength = HeaderSize + payload.Length;
        var span = writer.GetSpan(totalLength);

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)type;

        if (payload.Length > 0)
        {
            payload.CopyTo(span.Slice(HeaderSize));
        }

        writer.Advance(totalLength);
    }

    /// <summary>
    /// Frames a message into an exact-size rented <see cref="Payload"/>. The caller owns the result.
    /// </summary>
    public static Payload FrameToPayload(int messageId, MessageType type, ReadOnlySpan<byte> payload)
    {
        var totalLength = HeaderSize + payload.Length;
        var result = Payload.Rent(totalLength);
        var span = result.Memory.Span;

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)type;

        if (payload.Length > 0)
        {
            payload.CopyTo(span.Slice(HeaderSize));
        }

        return result;
    }

    /// <summary>
    /// Serializes <paramref name="body"/> directly behind a frame header into a single pooled
    /// buffer, then patches the total length. The caller owns the returned <see cref="Payload"/>.
    /// </summary>
    public static Payload FrameMessage<T>(ISerializer serializer, int messageId, MessageType type, T body)
    {
        using var writer = new PooledBufferWriter(HeaderSize);

        // Reserve the header; the total-length field is patched in after the body is serialized.
        var header = writer.GetSpan(HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), messageId);
        header[8] = (byte)type;
        writer.Advance(HeaderSize);

        serializer.Serialize(writer, body);

        var payload = writer.DetachPayload();
        BinaryPrimitives.WriteInt32LittleEndian(payload.Memory.Span.Slice(0, 4), payload.Length);
        return payload;
    }

    /// <summary>
    /// Parses a frame out of an in-memory buffer without copying. <paramref name="payload"/> is a
    /// slice of <paramref name="source"/> and shares its lifetime.
    /// </summary>
    public static bool TryReadFrame(
        ReadOnlyMemory<byte> source,
        out int messageId,
        out MessageType type,
        out ReadOnlyMemory<byte> payload)
    {
        messageId = 0;
        type = default;
        payload = ReadOnlyMemory<byte>.Empty;

        if (source.Length < HeaderSize)
        {
            return false;
        }

        var span = source.Span;
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
        if (totalLength < HeaderSize || totalLength > source.Length)
        {
            return false;
        }

        messageId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        type = (MessageType)span[8];
        payload = source.Slice(HeaderSize, totalLength - HeaderSize);
        return true;
    }

    /// <summary>
    /// Reads a framed message from a stream. Returns <c>null</c> when the connection is closed.
    /// The caller owns the returned <see cref="FramedMessage.Payload"/> and must dispose it.
    /// </summary>
    public static async Task<FramedMessage?> ReadMessageAsync(
        Stream stream,
        CancellationToken ct = default)
    {
        var headerBuffer = ArrayPool<byte>.Shared.Rent(HeaderSize);
        try
        {
            var bytesRead = await ReadExactAsync(stream, headerBuffer.AsMemory(0, HeaderSize), ct);
            if (bytesRead < HeaderSize)
            {
                return null; // Connection closed
            }

            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(0, 4));
            if (totalLength < HeaderSize || totalLength > MaxMessageSize)
            {
                throw new InvalidOperationException($"Invalid message length: {totalLength}");
            }

            var messageId = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(4, 4));
            var messageType = (MessageType)headerBuffer[8];

            var payloadLength = totalLength - HeaderSize;
            var payload = Payload.Rent(payloadLength);

            if (payloadLength > 0)
            {
                try
                {
                    bytesRead = await ReadExactAsync(stream, payload.Memory, ct);
                    if (bytesRead < payloadLength)
                    {
                        payload.Dispose();
                        return null; // Connection closed
                    }
                }
                catch
                {
                    payload.Dispose();
                    throw;
                }
            }

            return new FramedMessage(messageId, messageType, payload);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    /// <summary>
    /// Writes a framed message to a stream.
    /// </summary>
    public static async Task WriteMessageAsync(
        Stream stream,
        int messageId,
        MessageType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct = default)
    {
        using var writer = new PooledBufferWriter(HeaderSize + payload.Length);
        WriteFrame(writer, messageId, type, payload.Span);
        await stream.WriteAsync(writer.WrittenMemory, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(totalRead), ct);
            if (read == 0)
            {
                return totalRead; // Connection closed
            }
            totalRead += read;
        }
        return totalRead;
    }
}
