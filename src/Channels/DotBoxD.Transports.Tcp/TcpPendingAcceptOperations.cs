using System.Net.Sockets;

namespace DotBoxD.Transports.Tcp;

internal static class TcpPendingAcceptOperations
{
    public static Task<TcpClient> Start(TcpListener listener)
    {
        try
        {
            return listener.AcceptTcpClientAsync();
        }
        catch (Exception error)
        {
            // A stopped listener can fail synchronously; use the same lifecycle mapping as async failures.
            return Task.FromException<TcpClient>(error);
        }
    }

    public static void Observe(Task<TcpClient>? pending)
    {
        // Reclaim an in-flight accept stashed on cancellation. Stopping the listener usually faults it,
        // but a client can connect in the window before Stop(), completing with a live socket.
        _ = pending?.ContinueWith(
            static t =>
            {
                if (t.IsFaulted)
                {
                    _ = t.Exception;
                }
                else if (t.Status == TaskStatus.RanToCompletion)
                {
                    CloseAcceptedClient(t);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void CloseAcceptedClient(Task<TcpClient> completed)
    {
        try
        {
            completed.Result?.Dispose();
        }
        catch
        {
            // Best-effort close of a socket accepted during shutdown.
        }
    }
}
