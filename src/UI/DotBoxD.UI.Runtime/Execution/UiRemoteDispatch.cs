namespace DotBoxD.UI.Runtime;

internal static class UiRemoteDispatch
{
    internal static async ValueTask<UiStatePatch> DispatchAsync(
        IUiRemoteTransport transport, UiRemoteEvent message, CancellationToken token, CancellationToken caller, UiSession session)
    {
        try
        {
            using var callback = session.EnterCallbackScope();
            return await transport.DispatchAsync(message, token).AsTask().WaitAsync(token).ConfigureAwait(false);
        }
        catch when (caller.IsCancellationRequested)
        {
            caller.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort teardown must not replace the remote transport failure.
            }
            throw;
        }
    }
}
