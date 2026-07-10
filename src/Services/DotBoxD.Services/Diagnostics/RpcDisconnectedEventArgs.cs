using DotBoxD.Services.Peer;

namespace DotBoxD.Services.Diagnostics;

/// <summary>
/// Describes why a <see cref="RpcPeer"/>'s read loop ended.
/// </summary>
public sealed class RpcDisconnectedEventArgs : EventArgs
{
    public RpcDisconnectedEventArgs(string remoteEndpoint, Exception? error)
    {
        RemoteEndpoint = DiagnosticArgumentGuard.RequireNonBlank(
            remoteEndpoint,
            nameof(remoteEndpoint),
            "Remote endpoint");
        Error = error;
    }

    /// <summary>The remote endpoint string of the channel that closed.</summary>
    public string RemoteEndpoint { get; }

    /// <summary>The read error, or <see langword="null"/> when the close was graceful.</summary>
    public Exception? Error { get; }
}
