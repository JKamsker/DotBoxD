using DotBoxD.Services.Serialization;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services;

public static class RpcTransportExtensions
{
    public static Task<RpcPeerSession> ConnectPeerAsync(
        this ITransport transport,
        ISerializer serializer,
        RpcPeerOptions? options = null,
        CancellationToken ct = default) =>
        RpcPeerSession.ConnectAsync(transport, serializer, options, ct);

    public static Task<RpcPeerSession> ConnectPeerAsync(
        this ITransport transport,
        ISerializer serializer,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null,
        CancellationToken ct = default) =>
        RpcPeerSession.ConnectAsync(transport, serializer, configurePeer, options, ct);
}
