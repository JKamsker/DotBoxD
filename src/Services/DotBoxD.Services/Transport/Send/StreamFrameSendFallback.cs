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
        try
        {
            while (true)
            {
                await pendingOperation.ConfigureAwait(false);
                if (StreamFrameSendDriver.ResumeAfterCompletion(
                    ref state,
                    out pendingOperation))
                {
                    return;
                }
            }
        }
        finally
        {
            StreamFrameSendCleanup.FinishOrThrow(ref state);
        }
    }
}
