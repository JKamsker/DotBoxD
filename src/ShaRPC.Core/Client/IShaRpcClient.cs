namespace ShaRPC.Core.Client;

/// <summary>
/// Interface for the ShaRPC client. The invoke surface now lives on
/// <see cref="ShaRPC.Core.IRpcInvoker"/>; this interface adds the client-only
/// connect/lifecycle members on top. Generated proxies depend on
/// <see cref="ShaRPC.Core.IRpcInvoker"/>, so any <see cref="IShaRpcClient"/> (or
/// <see cref="ShaRPC.Core.RpcPeer"/>) can back a proxy.
/// </summary>
public interface IShaRpcClient : ShaRPC.Core.IRpcInvoker, IAsyncDisposable
{
    /// <summary>
    /// Connects to the server.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets whether the client is connected.
    /// </summary>
    bool IsConnected { get; }
}
