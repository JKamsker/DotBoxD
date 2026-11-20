using System.Runtime.ExceptionServices;

namespace DotBoxD.Services.Transport;

/// <summary>Task-backed overflow when every reusable Stream send source is unavailable.</summary>
internal static class StreamFrameSendFallback
{
    public static ValueTask Start(
        StreamFrameSendState state,
        ValueTask pendingOperation) =>
        ContinueAsync(state, pendingOperation);

    private static async ValueTask ContinueAsync(
        StreamFrameSendState state,
        ValueTask pendingOperation)
    {
        Exception? error = null;
        try
        {
            while (true)
            {
                await pendingOperation.ConfigureAwait(false);
                if (StreamFrameSendDriver.ResumeAfterCompletion(
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

        // Cleanup runs before completion is published and decides the exception the send reports.
        if (StreamFrameSendCleanup.Complete(ref state, error) is { } failure)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }
}
