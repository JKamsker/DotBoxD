using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostPeerConnectedDisposalRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task PeerConnectedHandlerDisposesPeer_HandoffLeavesAcceptedPeerDisposed()
    {
        var connection = new ScriptedConnection();
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RpcPeer? acceptedPeer = null;

        await using var host = RpcHost.Listen(new SingleConnectionServerTransport(connection), NewSerializer());
        host.PeerConnected += (_, args) =>
        {
            acceptedPeer = args.Peer;
            _ = args.Peer.DisposeAsync();
            connected.TrySetResult(true);
        };

        await host.StartAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(acceptedPeer);
        Assert.True(acceptedPeer.IsDisposed);
    }

    [Fact]
    public async Task PeerConstructionFailure_DisposesAcceptedConnection()
    {
        var connection = new ScriptedConnection();
        var failure = new InvalidOperationException("peer construction failed");
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost.Listen(new SingleConnectionServerTransport(connection), NewSerializer());
        host._peerFactoryForTest = _ => throw failure;
        host.AcceptError += (_, args) => error.TrySetResult(args.Error);

        await host.StartAsync();

        Assert.Same(failure, await error.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(connection.IsConnected);
    }
}
