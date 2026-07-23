using System.Runtime.ExceptionServices;

namespace DotBoxD.Transports.Tcp;

/// <summary>Task-backed overflow for TCP sends when the reusable population is unavailable.</summary>
internal static class TcpFrameSendFallback
{
    private static int s_startedCount;

    internal static int StartedCountForTests => Volatile.Read(ref s_startedCount);

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
}
