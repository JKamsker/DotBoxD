using DotBoxD.Plugins.Debugging;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;

namespace DotBoxD.Pushdown.Services;

/// <summary>Transport-neutral projection of the stable host-provided debug byte endpoint.</summary>
public interface IPluginDebugControlRpcService
{
    ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default);
}

/// <summary>Transport-neutral projection of the stable plugin-provided reverse debug event endpoint.</summary>
public interface IPluginDebugEventRpcService
{
    ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default);
}

/// <summary>Opt-in provisioning flag for <see cref="PluginConnectionHost{TConnection}"/>.</summary>
public sealed record PluginConnectionDebugOptions(bool Enabled = true);

internal sealed class PluginDebugControlRpcAdapter(IPluginDebugControlEndpoint endpoint)
    : IPluginDebugControlRpcService
{
    public ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default)
        => endpoint.ExchangeAsync(message, cancellationToken);
}

internal sealed class PluginDebugEventRpcAdapter(IPluginDebugEventRpcService service)
    : IPluginDebugEventEndpoint
{
    public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
        => service.PublishAsync(message, cancellationToken);
}

/// <summary>Convenience methods over the public proxy and dispatcher primitives.</summary>
public static class PluginDebugRpcPeerExtensions
{
    public static RpcPeer ProvidePluginDebugControl(
        this RpcPeer peer,
        IPluginDebugControlRpcService implementation) =>
        peer.Provide((IServiceDispatcher)new PluginDebugControlRpcDispatcher(implementation));

    public static IPluginDebugControlRpcService GetPluginDebugControl(this RpcPeer peer) =>
        new PluginDebugControlRpcProxy(peer);

    public static RpcPeer ProvidePluginDebugEvents(
        this RpcPeer peer,
        IPluginDebugEventRpcService implementation) =>
        peer.Provide((IServiceDispatcher)new PluginDebugEventRpcDispatcher(implementation));

    public static IPluginDebugEventRpcService GetPluginDebugEvents(this RpcPeer peer) =>
        new PluginDebugEventRpcProxy(peer);
}
