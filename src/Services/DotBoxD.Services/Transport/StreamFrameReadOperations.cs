using System.Buffers.Binary;
using System.Runtime.CompilerServices;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BeginFrame(ref StreamFrameReceiveBuffer receiveBuffer)
    {
        receiveBuffer.BeginFrame();

        return receiveBuffer.ReadBodyWithLookahead && receiveBuffer.Count != 0
            ? receiveBuffer.PrefixBytesRemaining
            : LengthPrefixSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CancellationToken StartTimeout(
        FrameReadTimeoutSource? readTimeout,
        CancellationToken callerToken,
        TimeSpan idleTimeout,
        int remaining)
    {
        if (remaining == 0 || readTimeout is null)
        {
            return callerToken;
        }

        return readTimeout.Start(callerToken, idleTimeout);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CancellationToken RearmTimeout(
        FrameReadTimeoutSource? readTimeout,
        CancellationToken currentToken,
        TimeSpan idleTimeout,
        int remaining)
    {
        if (remaining > 0 && readTimeout is not null)
        {
            return readTimeout.Rearm(idleTimeout);
        }

        return currentToken;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Memory<byte> PrepareRead(
        ref StreamFrameReceiveBuffer receiveBuffer,
        byte[] lengthBuffer,
        Payload? payload,
        int remaining)
    {
        if (payload is null)
        {
            return receiveBuffer.ReadBodyWithLookahead && receiveBuffer.Count != 0
                ? receiveBuffer.PrepareRead()
                : lengthBuffer.AsMemory(LengthPrefixSize - remaining, remaining);
        }

        return receiveBuffer.ReadBodyWithLookahead
            ? receiveBuffer.PrepareRead()
            : payload.Memory.Slice(payload.Length - remaining, remaining);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Memory<byte> PrepareExactRead(
        byte[] lengthBuffer,
        Payload? payload,
        int remaining) =>
        payload is null
            ? lengthBuffer.AsMemory(LengthPrefixSize - remaining, remaining)
            : payload.Memory.Slice(payload.Length - remaining, remaining);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ObservePendingRead(
        ref StreamFrameReceiveBuffer receiveBuffer,
        Payload? payload,
        bool completedSynchronously)
    {
        if (payload is null &&
            receiveBuffer.ReadBodyWithLookahead &&
            receiveBuffer.Count == 0 &&
            !completedSynchronously)
        {
            receiveBuffer.DisableBodyLookahead();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CommitRead(
        ref StreamFrameReceiveBuffer receiveBuffer,
        Payload? payload,
        int remaining,
        int read)
    {
        if (payload is null)
        {
            return receiveBuffer.ReadBodyWithLookahead && receiveBuffer.Count != 0
                ? receiveBuffer.CommitRead(read)
                : remaining - read;
        }

        if (!receiveBuffer.ReadBodyWithLookahead)
        {
            return remaining - read;
        }

        receiveBuffer.CommitBodyRead(read);
        return receiveBuffer.CopyBodyTo(payload, remaining);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InitializePayload(
        ref StreamFrameReceiveBuffer receiveBuffer,
        byte[] lengthBuffer,
        int maxMessageSize,
        out Payload payload)
    {
        var totalLength = receiveBuffer.ReadBodyWithLookahead && receiveBuffer.Count != 0
            ? receiveBuffer.ConsumeLengthPrefix()
            : BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        MessageFrameReader.ValidateIncomingFrameLength(totalLength, maxMessageSize);

        payload = Payload.Rent(totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Memory.Span, totalLength);
        var remaining = totalLength - LengthPrefixSize;
        if (receiveBuffer.ReadBodyWithLookahead)
        {
            remaining = receiveBuffer.CopyBodyTo(payload, remaining);
            receiveBuffer.ApplyFrameLength(totalLength);
        }

        return remaining;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InitializeExactPayload(
        byte[] lengthBuffer,
        int maxMessageSize,
        out Payload payload)
    {
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        MessageFrameReader.ValidateIncomingFrameLength(totalLength, maxMessageSize);
        payload = Payload.Rent(totalLength);
        lengthBuffer.CopyTo(payload.Memory.Span);
        return totalLength - LengthPrefixSize;
    }

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

    public static async ValueTask DisposeBestEffortAsync(Stream stream)
    {
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Closing a transport is best-effort.
        }
    }
}
