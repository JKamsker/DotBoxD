using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Peer;

internal static class RpcPeerCancellation
{
    internal static void CancelReadLoop(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (Exception exception)
        {
            RpcDiagnostics.Report("Read loop cancellation during peer teardown failed", exception);
        }
    }
}
