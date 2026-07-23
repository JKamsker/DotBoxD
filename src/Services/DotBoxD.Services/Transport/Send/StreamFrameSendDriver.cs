using System.IO.Pipes;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

/// <summary>Advances one StreamConnection owned-frame send until completion or suspension.</summary>
internal static class StreamFrameSendDriver
{
    public static void Initialize(
        StreamConnection connection,
        PooledBufferWriter frame,
        CancellationToken cancellationToken,
        ref StreamFrameSendState state)
    {
        state.Connection = connection;
        state.Frame = frame;
        state.CallerToken = cancellationToken;
        state.Stage = StreamFrameSendStage.AcquireGate;

        var data = frame.WrittenMemory;
        connection.ThrowIfDisposedForSend();
        cancellationToken.ThrowIfCancellationRequested();
        MessageFramer.ValidateOutgoingFrame(data.Span, connection.MaxOutgoingMessageSize);
        state.Data = data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAdvance(
        ref StreamFrameSendState state,
        out ValueTask pendingOperation)
    {
        while (state.Stage != StreamFrameSendStage.Completed)
        {
            pendingOperation = StartCurrentStage(ref state);
            var awaiter = pendingOperation.ConfigureAwait(false).GetAwaiter();
            if (!awaiter.IsCompleted)
            {
                return false;
            }

            awaiter.GetResult();
            CompleteCurrentStage(ref state);
        }

        pendingOperation = default;
        return true;
    }

    public static bool Resume(
        ref StreamFrameSendState state,
        ValueTask pendingOperation,
        out ValueTask nextPendingOperation)
    {
        pendingOperation.ConfigureAwait(false).GetAwaiter().GetResult();
        return ResumeAfterCompletion(ref state, out nextPendingOperation);
    }

    public static bool ResumeAfterCompletion(
        ref StreamFrameSendState state,
        out ValueTask nextPendingOperation)
    {
        CompleteCurrentStage(ref state);
        return TryAdvance(ref state, out nextPendingOperation);
    }

    private static ValueTask StartCurrentStage(ref StreamFrameSendState state)
    {
        var connection = RequireConnection(ref state);
        return state.Stage switch
        {
            StreamFrameSendStage.AcquireGate => TransportSendGate.WaitAsync(
                connection.SendGate,
                connection.SendDisposalToken,
                state.CallerToken,
                nameof(StreamConnection)),
            StreamFrameSendStage.Write => connection.SendStream.WriteAsync(
                state.Data,
                state.CallerToken),
            StreamFrameSendStage.Flush => new ValueTask(
                connection.SendStream.FlushAsync(state.CallerToken)),
            _ => throw new InvalidOperationException("The stream send has no stage to start."),
        };
    }

    private static void CompleteCurrentStage(ref StreamFrameSendState state)
    {
        var connection = RequireConnection(ref state);
        switch (state.Stage)
        {
            case StreamFrameSendStage.AcquireGate:
                state.GateHeld = true;
                state.CallerToken.ThrowIfCancellationRequested();
                connection.ThrowIfDisposedForSend();
                state.Stage = StreamFrameSendStage.Write;
                break;
            case StreamFrameSendStage.Write:
                state.Stage = connection.SendStream is PipeStream
                    ? StreamFrameSendStage.Completed
                    : StreamFrameSendStage.Flush;
                break;
            case StreamFrameSendStage.Flush:
                state.Stage = StreamFrameSendStage.Completed;
                break;
            default:
                throw new InvalidOperationException("The stream send completed an invalid stage.");
        }
    }

    private static StreamConnection RequireConnection(ref StreamFrameSendState state) =>
        state.Connection ?? throw new InvalidOperationException(
            "The stream send driver has no active connection.");
}
