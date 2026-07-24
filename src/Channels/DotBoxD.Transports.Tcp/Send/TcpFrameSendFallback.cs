using System.Runtime.ExceptionServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>Task-backed overflow for TCP sends when the reusable population is unavailable.</summary>
internal static class TcpFrameSendFallback
{
    private static int s_startedCount;
    private static int s_rawFromBeginningCount;

    internal static int StartedCountForTests => Volatile.Read(ref s_startedCount);
    internal static int RawFromBeginningCountForTests =>
        Volatile.Read(ref s_rawFromBeginningCount);

    public static ValueTask StartRawFromBeginning(
        TcpConnection connection,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken) =>
        ContinueRawFromBeginningAsync(connection, data, cancellationToken);

    public static ValueTask Start(
        ref TcpFrameSendState state,
        ValueTask pendingOperation)
    {
        Interlocked.Increment(ref s_startedCount);
        var transferredState = state;
        state.Clear();
        return new ValueTask(ContinueAsync(transferredState, pendingOperation));
    }

    public static ValueTask CreateFailure(Exception error) =>
        new(AwaitFailureAsync(error));

    private static async Task ContinueAsync(
        TcpFrameSendState state,
        ValueTask pendingOperation)
    {
        Exception? error = null;
        try
        {
            while (true)
            {
                await pendingOperation.ConfigureAwait(false);
                if (TcpFrameSendDriver.ResumeAfterPending(
                        ref state,
                        out pendingOperation))
                {
                    break;
                }
            }
        }
        catch (Exception caught)
        {
            error = caught;
        }

        var cleanupError = TcpFrameSendDriver.FinishAndClear(ref state);
        error = cleanupError ?? error;
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static async Task AwaitFailureAsync(Exception error)
    {
        await Task.FromException(error).ConfigureAwait(false);
    }

    private static async ValueTask ContinueRawFromBeginningAsync(
        TcpConnection connection,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        // The compact raw-send state machine is cheaper than transferring the larger shared state
        // after the bounded reusable population is exhausted. Start it before claiming a gate so
        // saturated raw fan-out keeps its established allocation profile.
        Interlocked.Increment(ref s_rawFromBeginningCount);
        connection.ThrowIfDisposedForSend();
        cancellationToken.ThrowIfCancellationRequested();
        MessageFramer.ValidateOutgoingFrame(data.Span);

        await TransportSendGate.WaitAsync(connection.SendGate, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            connection.ThrowIfDisposedForSend();
            await connection.SendStream.WriteAsync(data, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            connection.ReleaseSendGate();
        }
    }
}
