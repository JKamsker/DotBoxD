using System.Runtime.ExceptionServices;
using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Server;

internal static class RpcHostDisposeCoordinator
{
    internal static async ValueTask DisposeAsync(
        Func<Task> stop,
        Func<Task> closePeers,
        Func<Task> awaitPeerCleanup,
        Func<Task> disposeListener)
    {
        Exception? failure = null;

        try
        {
            await stop().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = CaptureDisposeFailure(failure, "RpcHost.StopException", ex);
        }

        try
        {
            await closePeers().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = CaptureDisposeFailure(failure, "RpcHost.PeerCloseException", ex);
        }

        try
        {
            await awaitPeerCleanup().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = CaptureDisposeFailure(failure, "RpcHost.PeerCleanupException", ex);
        }

        try
        {
            await disposeListener().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = CaptureDisposeFailure(failure, "RpcHost.ListenerDisposeException", ex);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Exception CaptureDisposeFailure(Exception? currentFailure, string dataKey, Exception ex)
    {
        if (currentFailure is null)
        {
            return ex;
        }

        try
        {
            currentFailure.Data[dataKey] = ex;
        }
        catch (Exception dataEx)
        {
            RpcDiagnostics.Report("RpcHost dispose failure capture failed", dataEx);
            RpcDiagnostics.Report("Suppressed RpcHost dispose failure", ex);
        }

        return currentFailure;
    }
}
