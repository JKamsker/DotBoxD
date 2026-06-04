using System.Diagnostics;

namespace ShaRPC.Core;

/// <summary>
/// Central diagnostic hooks for errors ShaRPC observes on best-effort paths.
/// </summary>
public static class RpcDiagnostics
{
    /// <summary>
    /// Raised when ShaRPC observes an error that cannot be thrown to the original caller.
    /// Diagnostic event handlers are isolated from each other and from RPC internals.
    /// </summary>
    public static event EventHandler<RpcDiagnosticErrorEventArgs>? Error;

    internal static void Report(string operation, Exception error)
    {
        // Build the message inside the guard: a custom exception's virtual Message getter can throw, and
        // that must not escape Report and break subscriber isolation.
        SafeTrace(() => $"{operation}: {error.GetType().Name}: {error.Message}");

        var handler = Error;
        if (handler is null)
        {
            return;
        }

        var args = new RpcDiagnosticErrorEventArgs(operation, error);
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<RpcDiagnosticErrorEventArgs>)subscriber).Invoke(typeof(RpcDiagnostics), args);
            }
            catch (Exception subscriberError)
            {
                // A faulting subscriber's own ToString() can throw too, so format inside the guard.
                SafeTrace(() => $"ShaRPC diagnostic handler failed: {subscriberError}");
            }
        }
    }

    private static void SafeTrace(Func<string> messageFactory)
    {
        try
        {
            Trace.TraceError(messageFactory());
        }
        catch
        {
            // Best-effort fallback logging only: neither building the message (a virtual Message/ToString
            // may throw) nor a hostile/faulting TraceListener must break diagnostic subscriber isolation
            // by propagating out of Report.
        }
    }
}
