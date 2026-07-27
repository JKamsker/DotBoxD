using System.Runtime.CompilerServices;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

/// <summary>Advances one StreamConnection frame read until it completes or genuinely suspends.</summary>
internal static class StreamFrameReceiveDriver
{
    public static void Initialize(
        StreamConnection connection,
        CancellationToken ct,
        ref FrameReceiveOperationState state)
    {
        ref var receiveBuffer = ref connection.FrameReceiveBuffer;
        state.CallerToken = ct;
        state.ReadToken = ct;
        state.WriterBacked = receiveBuffer.WriterBackedOwner;

        connection.ThrowIfDisposedForReceive();
        state.Remaining = connection.UseFrameReceiveLookahead
            ? StreamFrameReadOperations.BeginFrame(ref receiveBuffer)
            : StreamFrameReadOperations.LengthPrefixSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAdvance(
        StreamConnection connection,
        ref FrameReceiveOperationState state,
        out RpcFrame frame,
        out ValueTask<int> pendingRead)
    {
        while (true)
        {
            if (state.Remaining == 0 && AdvanceCompletedPhase(connection, ref state, out frame))
            {
                pendingRead = default;
                return true;
            }

            if (!state.PhaseStarted)
            {
                StartPhase(connection, ref state);
            }

            int read;
            try
            {
                pendingRead = ReadChunk(connection, ref state);
                var awaiter = pendingRead.ConfigureAwait(false).GetAwaiter();
                if (!awaiter.IsCompleted)
                {
                    frame = default;
                    return false;
                }

                read = awaiter.GetResult();
            }
            catch (OperationCanceledException) when (
                StreamFrameReadOperations.IsTimeoutCancellation(connection.FrameReceiveTimeout))
            {
                throw FrameReadTimeoutSource.CreateTimeoutException(connection.FrameReadIdleTimeout);
            }

            if (CommitRead(connection, ref state, read, out frame))
            {
                pendingRead = default;
                return true;
            }
        }
    }

    public static bool Resume(
        StreamConnection connection,
        ref FrameReceiveOperationState state,
        ValueTask<int> pendingRead,
        out RpcFrame frame,
        out ValueTask<int> nextPendingRead)
    {
        var read = GetReadResult(connection, pendingRead.ConfigureAwait(false).GetAwaiter());
        nextPendingRead = default;
        if (CommitRead(connection, ref state, read, out frame))
        {
            return true;
        }

        return TryAdvance(connection, ref state, out frame, out nextPendingRead);
    }

    public static bool ResumeAfterRead(
        StreamConnection connection,
        ref FrameReceiveOperationState state,
        int read,
        out RpcFrame frame,
        out ValueTask<int> nextPendingRead)
    {
        nextPendingRead = default;
        if (CommitRead(connection, ref state, read, out frame))
        {
            return true;
        }

        return TryAdvance(connection, ref state, out frame, out nextPendingRead);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StartPhase(
        StreamConnection connection,
        ref FrameReceiveOperationState state)
    {
        state.ReadToken = StreamFrameReadOperations.StartTimeout(
            connection.FrameReceiveTimeout,
            state.CallerToken,
            connection.FrameReadIdleTimeout,
            state.Remaining);
        state.PhaseStarted = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValueTask<int> ReadChunk(
        StreamConnection connection,
        ref FrameReceiveOperationState state)
    {
        ref var receiveBuffer = ref connection.FrameReceiveBuffer;
        if (!connection.UseFrameReceiveLookahead)
        {
            return connection.FrameReceiveStream.ReadAsync(
                StreamFrameReadOperations.PrepareExactRead(
                    connection.FrameReceiveLengthBuffer,
                    ref state.Owner,
                    state.WriterBacked,
                    state.Remaining),
                state.ReadToken);
        }

        var pendingRead = connection.FrameReceiveStream.ReadAsync(
            StreamFrameReadOperations.PrepareRead(
                ref receiveBuffer,
                connection.FrameReceiveLengthBuffer,
                ref state.Owner,
                state.Remaining),
            state.ReadToken);
        StreamFrameReadOperations.ObservePendingRead(
            ref receiveBuffer,
            state.Owner,
            pendingRead.IsCompletedSuccessfully);
        return pendingRead;
    }

    private static int GetReadResult(
        StreamConnection connection,
        ConfiguredValueTaskAwaitable<int>.ConfiguredValueTaskAwaiter awaiter)
    {
        try
        {
            return awaiter.GetResult();
        }
        catch (OperationCanceledException) when (
            StreamFrameReadOperations.IsTimeoutCancellation(connection.FrameReceiveTimeout))
        {
            throw FrameReadTimeoutSource.CreateTimeoutException(connection.FrameReadIdleTimeout);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CommitRead(
        StreamConnection connection,
        ref FrameReceiveOperationState state,
        int read,
        out RpcFrame frame)
    {
        if (read == 0)
        {
            frame = StreamFrameReadOperations.HandleEndOfStream(
                state.Owner,
                state.Remaining,
                StreamFrameReadProgressFormat.WholeFrame);
            return true;
        }

        ref var receiveBuffer = ref connection.FrameReceiveBuffer;
        if (connection.UseFrameReceiveLookahead)
        {
            state.Remaining = StreamFrameReadOperations.CommitRead(
                ref receiveBuffer,
                ref state.Owner,
                state.Remaining,
                read);
        }
        else
        {
            state.Owner.AdvanceBodyRead(read, state.WriterBacked);
            state.Remaining -= read;
        }

        state.ReadToken = StreamFrameReadOperations.RearmTimeout(
            connection.FrameReceiveTimeout,
            state.ReadToken,
            connection.FrameReadIdleTimeout,
            state.Remaining);
        if (state.Remaining != 0)
        {
            frame = default;
            return false;
        }

        return AdvanceCompletedPhase(connection, ref state, out frame);
    }

    private static bool AdvanceCompletedPhase(
        StreamConnection connection,
        ref FrameReceiveOperationState state,
        out RpcFrame frame)
    {
        if (state.Owner.IsAllocated)
        {
            frame = state.Owner.TransferFrame(state.WriterBacked);
            return true;
        }

        ref var receiveBuffer = ref connection.FrameReceiveBuffer;
        state.Remaining = connection.UseFrameReceiveLookahead
            ? StreamFrameReadOperations.InitializeOwner(
                ref receiveBuffer,
                connection.FrameReceiveLengthBuffer,
                connection.MaxIncomingFrameSize,
                state.WriterBacked,
                ref state.Owner)
            : StreamFrameReadOperations.InitializeExactOwner(
                connection.FrameReceiveLengthBuffer,
                connection.MaxIncomingFrameSize,
                state.WriterBacked,
                ref state.Owner);
        state.PhaseStarted = false;
        if (state.Remaining != 0)
        {
            frame = default;
            return false;
        }

        frame = state.Owner.TransferFrame(state.WriterBacked);
        return true;
    }
}
