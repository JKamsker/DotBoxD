using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Server;

internal static class RpcHostCancellation
{
    /// <summary>
    /// Starts cancellation away from the caller so a synchronous cancellation callback can safely
    /// re-enter host shutdown. Such a callback may wait for the current stop task, which would
    /// deadlock if this method invoked <see cref="CancellationTokenSource.Cancel()"/> inline.
    /// </summary>
    internal static Task TryCancelAsync(CancellationTokenSource cts) =>
        Task.Run(() => TryCancel(cts));

    /// <summary>
    /// Waits only until cancellation has been requested, not until callbacks have returned. This
    /// keeps the host in its stopping state while a re-entrant callback waits for that stop.
    /// </summary>
    internal static async Task WaitForCancellationRequestAsync(CancellationTokenSource cts, Task cancellation)
    {
        while (!cts.IsCancellationRequested && !cancellation.IsCompleted)
        {
            await Task.Yield();
        }
    }

    internal static void DisposeAfterCancellation(CancellationTokenSource cts, Task cancellation)
    {
        if (cancellation.IsCompleted)
        {
            Dispose(cts);
            return;
        }

        _ = cancellation.ContinueWith(
            static (_, source) => Dispose((CancellationTokenSource)source!),
            cts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void TryCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CTS was disposed by a prior failed stop attempt.
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Host cancellation callback failed during shutdown", ex);
        }
    }

    internal static void Dispose(CancellationTokenSource cts)
    {
        try
        {
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a prior failed stop attempt.
        }
    }
}
