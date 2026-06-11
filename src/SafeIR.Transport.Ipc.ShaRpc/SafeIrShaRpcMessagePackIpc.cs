namespace SafeIR.Transport.Ipc;

using ShaRPC.Core;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;

public static class SafeIrShaRpcMessagePackIpc
{
    private static readonly MessagePackRpcSerializer Serializer = new();
    private static readonly RpcPeerOptions DefaultClientOptions = new() {
        RequestTimeout = TimeSpan.FromSeconds(10),
        RejectInboundCalls = true
    };

    public static RpcHost ListenNamedPipe(
        string pipeName,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(configurePeer);
        return RpcHost
            .Listen(new NamedPipeServerTransport(pipeName), Serializer, options)
            .ForEachPeer(configurePeer);
    }

    public static ValueTask<SafeIrShaRpcClientPeer> ConnectNamedPipeAsync(
        string pipeName,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
        => ConnectNamedPipeAsync(".", pipeName, options, cancellationToken);

    public static async ValueTask<SafeIrShaRpcClientPeer> ConnectNamedPipeAsync(
        string serverName,
        string pipeName,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var transport = new NamedPipeClientTransport(serverName, pipeName);
        try {
            await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var peer = RpcPeer
                .Over(
                    transport.Connection!,
                    Serializer,
                    options ?? DefaultClientOptions)
                .Start();
            return new SafeIrShaRpcClientPeer(transport, peer);
        }
        catch {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

}
