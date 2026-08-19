using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Server;

internal static class RpcHostCancellation
{
    internal static void TryCancel(CancellationTokenSource cts)
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
