using System.Buffers;
using System.Buffers.Binary;
using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Protocol;

internal static class MessageStreamFramer
{
    public static async Task<MessageFramer.FramedMessage?> ReadMessageAsync(
        Stream stream,
        CancellationToken ct)
        => await ReadMessageAsync(stream, FrameReadTimeoutSource.DefaultIdleTimeout, ct).ConfigureAwait(false);

    public static async Task<MessageFramer.FramedMessage?> ReadMessageAsync(
        Stream stream,
        TimeSpan frameReadIdleTimeout,
        CancellationToken ct)
    {
        ProtocolArgumentGuard.ThrowIfNull(stream, nameof(stream));

        ct.ThrowIfCancellationRequested();

        var idleTimeout = FrameReadTimeoutSource.Validate(
            frameReadIdleTimeout,
            nameof(frameReadIdleTimeout));
        using var readTimeout = idleTimeout == Timeout.InfiniteTimeSpan
            ? null
            : new FrameReadTimeoutSource();
        var headerBuffer = ArrayPool<byte>.Shared.Rent(MessageFramer.HeaderSize);
        try
        {
            var bytesRead = await ReadExactAsync(
                stream,
                headerBuffer.AsMemory(0, MessageFramer.HeaderSize),
                ct,
                readTimeout,
                idleTimeout).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return null;
            }

            if (bytesRead < MessageFramer.HeaderSize)
            {
                throw new InvalidDataException(
                    $"Connection closed after {bytesRead} of {MessageFramer.HeaderSize} frame header bytes.");
            }

            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(0, 4));
            if (totalLength < MessageFramer.HeaderSize || totalLength > MessageFramer.MaxMessageSize)
            {
                throw new InvalidDataException($"Invalid DotBoxD frame length: {totalLength}.");
            }

            var messageType = (MessageType)headerBuffer[8];
            if (!MessageFrameReader.IsDefinedMessageType(messageType))
            {
                throw new InvalidDataException($"Invalid DotBoxD message type: 0x{headerBuffer[8]:X2}.");
            }

            var messageId = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(4, 4));
            var payload = await ReadPayloadAsync(stream, totalLength, ct, readTimeout, idleTimeout)
                .ConfigureAwait(false);
            return new MessageFramer.FramedMessage(messageId, messageType, payload);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    public static async Task WriteMessageAsync(
        Stream stream,
        int messageId,
        MessageType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        ProtocolArgumentGuard.ThrowIfNull(stream, nameof(stream));

        ct.ThrowIfCancellationRequested();

        var totalLength = MessageFrameReader.GetOutgoingFrameLength(payload.Length);
        using var writer = PooledBufferWriter.Rent(totalLength);
        MessageFramer.WriteFrame(writer, messageId, type, payload.Span);
        await stream.WriteAsync(writer.WrittenMemory, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<Payload> ReadPayloadAsync(
        Stream stream,
        int totalLength,
        CancellationToken ct,
        FrameReadTimeoutSource? readTimeout,
        TimeSpan idleTimeout)
    {
        var payloadLength = totalLength - MessageFramer.HeaderSize;
        var payload = Payload.Rent(payloadLength);

        if (payloadLength == 0)
        {
            return payload;
        }

        try
        {
            var bytesRead = await ReadExactAsync(stream, payload.Memory, ct, readTimeout, idleTimeout)
                .ConfigureAwait(false);
            if (bytesRead < payloadLength)
            {
                payload.Dispose();
                throw new InvalidDataException(
                    $"Connection closed after {bytesRead} of {payloadLength} payload bytes.");
            }
        }
        catch
        {
            payload.Dispose();
            throw;
        }

        return payload;
    }

    private static async Task<int> ReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken ct,
        FrameReadTimeoutSource? readTimeout,
        TimeSpan idleTimeout)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var remaining = buffer.Slice(totalRead);
            var read = readTimeout is null
                ? await stream.ReadAsync(remaining, ct).ConfigureAwait(false)
                : await readTimeout.ReadAsync(stream, remaining, ct, idleTimeout).ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
