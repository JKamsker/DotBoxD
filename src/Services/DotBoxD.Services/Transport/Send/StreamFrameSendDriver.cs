using System.IO.Pipes;
using System.Runtime.CompilerServices;

namespace DotBoxD.Services.Transport;

/// <summary>Advances one StreamConnection owned-frame send until completion or suspension.</summary>
internal static class StreamFrameSendDriver
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAdvance(
        ref StreamFrameSendState state,
        out ValueTask pendingOperation)
    {
        var connection = RequireConnection(ref state);
        switch (state.Stage)
        {
            case StreamFrameSendStage.AcquireGate:
                pendingOperation = TransportSendGate.WaitAsync(
                    connection.SendGate,
                    state.CallerToken);
                if (!pendingOperation.IsCompleted)
                {
                    return false;
                }

                pendingOperation.GetAwaiter().GetResult();
                state.GateHeld = true;
                state.CallerToken.ThrowIfCancellationRequested();
                connection.ThrowIfDisposedForSend();
                state.Stage = StreamFrameSendStage.Write;
                goto case StreamFrameSendStage.Write;
            case StreamFrameSendStage.Write:
                pendingOperation = connection.SendStream.WriteAsync(
                    state.Data,
                    state.CallerToken);
                if (!pendingOperation.IsCompleted)
                {
                    return false;
                }

                pendingOperation.GetAwaiter().GetResult();
                if (connection.SendStream is PipeStream)
                {
                    state.Stage = StreamFrameSendStage.Completed;
                    pendingOperation = default;
                    return true;
                }

                state.Stage = StreamFrameSendStage.Flush;
                goto case StreamFrameSendStage.Flush;
            case StreamFrameSendStage.Flush:
                pendingOperation = new ValueTask(
                    connection.SendStream.FlushAsync(state.CallerToken));
                if (!pendingOperation.IsCompleted)
                {
                    return false;
                }

                pendingOperation.GetAwaiter().GetResult();
                state.Stage = StreamFrameSendStage.Completed;
                pendingOperation = default;
                return true;
            case StreamFrameSendStage.Completed:
                pendingOperation = default;
                return true;
            default:
                throw new InvalidOperationException("The stream send has no stage to start.");
        }
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
