using DotBoxD.Services.Peer;

namespace DotBoxD.Services.Diagnostics;

/// <summary>
/// Describes a non-cancellation failure in a <see cref="RpcPeer"/>'s read loop.
/// </summary>
public sealed class RpcReadErrorEventArgs : EventArgs
{
    public RpcReadErrorEventArgs(string remoteEndpoint, Exception error)
    {
        RemoteEndpoint = DiagnosticArgumentGuard.RequireNonBlank(
            remoteEndpoint,
            nameof(remoteEndpoint),
            "Remote endpoint");
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>The remote endpoint string of the channel that failed.</summary>
    public string RemoteEndpoint { get; }

    /// <summary>The read-loop exception.</summary>
    public Exception Error { get; }
}
