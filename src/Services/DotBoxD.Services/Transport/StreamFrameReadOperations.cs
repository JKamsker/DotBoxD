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
        ref StreamFrameReceiveOwner owner,
        int remaining)
    {
        if (!owner.IsAllocated)
        {
            return receiveBuffer.ReadBodyWithLookahead && receiveBuffer.Count != 0
                ? receiveBuffer.PrepareRead()
                : lengthBuffer.AsMemory(LengthPrefixSize - remaining, remaining);
        }

        return receiveBuffer.ReadBodyWithLookahead
            ? receiveBuffer.PrepareRead()
            : owner.PrepareBodyRead(remaining, receiveBuffer.WriterBackedOwner);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Memory<byte> PrepareExactRead(
        byte[] lengthBuffer,
        ref StreamFrameReceiveOwner owner,
        bool writerBacked,
        int remaining) =>
        !owner.IsAllocated
            ? lengthBuffer.AsMemory(LengthPrefixSize - remaining, remaining)
            : owner.PrepareBodyRead(remaining, writerBacked);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ObservePendingRead(
        ref StreamFrameReceiveBuffer receiveBuffer,
        StreamFrameReceiveOwner owner,
        bool completedSynchronously)
    {
        if (!owner.IsAllocated &&
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
        ref StreamFrameReceiveOwner owner,
        int remaining,
        int read)
    {
        if (!owner.IsAllocated)
        {
            return receiveBuffer.ReadBodyWithLookahead && receiveBuffer.Count != 0
                ? receiveBuffer.CommitRead(read)
                : remaining - read;
        }

        if (!receiveBuffer.ReadBodyWithLookahead)
        {
            owner.AdvanceBodyRead(read, receiveBuffer.WriterBackedOwner);
            return remaining - read;
        }

        receiveBuffer.CommitBodyRead(read);
        return receiveBuffer.CopyBodyTo(ref owner, remaining);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InitializeOwner(
        ref StreamFrameReceiveBuffer receiveBuffer,
        byte[] lengthBuffer,
        int maxMessageSize,
        bool writerBacked,
        ref StreamFrameReceiveOwner owner)
    {
        var totalLength = receiveBuffer.ReadBodyWithLookahead && receiveBuffer.Count != 0
            ? receiveBuffer.ConsumeLengthPrefix()
            : BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        MessageFrameReader.ValidateIncomingFrameLength(totalLength, maxMessageSize);

        owner.Initialize(totalLength, writerBacked);
        var remaining = totalLength - LengthPrefixSize;
        if (receiveBuffer.ReadBodyWithLookahead)
        {
            remaining = receiveBuffer.CopyBodyTo(ref owner, remaining);
            receiveBuffer.ApplyFrameLength(totalLength);
        }

        return remaining;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InitializeExactOwner(
        byte[] lengthBuffer,
        int maxMessageSize,
        bool writerBacked,
        ref StreamFrameReceiveOwner owner)
    {
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        MessageFrameReader.ValidateIncomingFrameLength(totalLength, maxMessageSize);
        owner.Initialize(totalLength, writerBacked, lengthBuffer);
        return totalLength - LengthPrefixSize;
    }

    public static bool IsTimeoutCancellation(FrameReadTimeoutSource? readTimeout) =>
        readTimeout is not null && readTimeout.IsCurrentOwnerTimeoutCancellation();

    public static ValueTask<RpcFrame> CreateCanceledReceive(CancellationToken ct) =>
        new(Task.FromCanceled<RpcFrame>(ct));

    public static ValueTask<RpcFrame> CreateFailedReceive(Exception error) =>
        new(Task.FromException<RpcFrame>(error));

    public static ValueTask<RpcFrame> CreateFailedReceive(
        ReceiveEnterFailure failure,
        string channelName) =>
        CreateFailedReceive(ReceiveConcurrencyGuard.CreateEnterException(failure, channelName));

    public static RpcFrame HandleEndOfStream(
        StreamFrameReceiveOwner owner,
        int remaining,
        StreamFrameReadProgressFormat progressFormat)
    {
        var targetLength = owner.GetTargetBodyLength(remaining);
        var bytesRead = owner.IsAllocated
            ? owner.GetBodyBytesRead(remaining)
            : targetLength - remaining;
        if (!owner.IsAllocated)
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
