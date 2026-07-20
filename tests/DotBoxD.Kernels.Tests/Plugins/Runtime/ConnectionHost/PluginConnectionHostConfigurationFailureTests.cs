using DotBoxD.Plugins;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Testing;
using DotBoxD.Services.Transport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginConnectionHostConfigurationFailureTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Configure_failure_rejects_peer_before_partial_services_are_invokable()
    {
        var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();
        using var server = PluginServer.Create();
        var sentinel = new InvalidOperationException("configuration failed after partial service registration");

        await using var host = await PluginConnectionHost<object>.StartAsync(
            server,
            new SingleConnectionServerTransport(serverChannel, ownsConnection: true),
            (peer, _) =>
            {
                peer.Provide<IConnectionHostProbeService>(new ConnectionHostProbeService());
                throw sentinel;
            });

        await using var session = await RpcMessagePackIpc.ConnectAsync(
            new SingleConnectionTransport(clientChannel, ownsConnection: true),
            new RpcPeerOptions { RequestTimeout = Timeout });

        var connectedFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Connected.WaitAsync(Timeout));
        Assert.Same(sentinel, connectedFailure);

        var probe = session.Get<IConnectionHostProbeService>();
        var invokeFailure = await Record.ExceptionAsync(
            () => probe.IncrementAsync(41).AsTask().WaitAsync(Timeout));

        Assert.NotNull(invokeFailure);
        Assert.IsNotType<TimeoutException>(invokeFailure);
    }

    private sealed class ConnectionHostProbeService : IConnectionHostProbeService
    {
        public ValueTask<int> IncrementAsync(int value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(value + 1);
        }
    }
}

[RpcService]
public interface IConnectionHostProbeService
{
    ValueTask<int> IncrementAsync(int value, CancellationToken cancellationToken = default);
}
