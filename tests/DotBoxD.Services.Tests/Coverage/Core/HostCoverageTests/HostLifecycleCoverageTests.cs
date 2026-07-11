using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.HostCoverageTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Behavioral coverage for <see cref="RpcHost"/> and its internal accept loop / peer collection.
/// Everything is driven through the public surface (RpcHost, the public
/// SingleConnectionServerTransport, RpcPeer over InMemoryPipe connections) so the internal
/// RpcHostAcceptLoop, RpcHostPeerCollection and RpcHostPeerConfiguration types are exercised only
/// as a consequence of real start/accept/disconnect/dispose scenarios.
/// </summary>
public sealed class HostLifecycleCoverageTests
{
    // ----- Listen argument validation (RpcHost lines 39-40, 44-45) -----

    [Fact]
    public void Listen_NullListener_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => RpcHost.Listen(null!, NewSerializer()));
        Assert.Equal("listener", ex.ParamName);
    }

    [Fact]
    public void Listen_NullSerializer_ThrowsArgumentNullException()
    {
        var connection = new ScriptedConnection();
        var ex = Assert.Throws<ArgumentNullException>(
            () => RpcHost.Listen(new SingleConnectionServerTransport(connection), null!));
        Assert.Equal("serializer", ex.ParamName);
    }

    [Fact]
    public void ForEachPeer_NullConfigure_ThrowsArgumentNullException()
    {
        var connection = new ScriptedConnection();
        var host = RpcHost.Listen(new SingleConnectionServerTransport(connection), NewSerializer());
        Assert.Throws<ArgumentNullException>(() => host.ForEachPeer(null!));
    }

    [Fact]
    public async Task ForEachPeer_AfterDispose_ThrowsObjectDisposedException()
    {
        var connection = new ScriptedConnection();
        var host = RpcHost.Listen(new SingleConnectionServerTransport(connection), NewSerializer());

        await host.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => host.ForEachPeer(_ => { }));
    }

    // ----- StartAsync lifecycle guards (RpcHost 80-81, 84-86, 125-128, 134-140, 167-169, 174) -----

    [Fact]
    public async Task StartAsync_CalledTwice_ThrowsInvalidOperationException()
    {
        var connection = new ScriptedConnection();
        await using var host = RpcHost.Listen(
            new SingleConnectionServerTransport(connection), NewSerializer());

        await host.StartAsync().WaitAsync(Timeout5s);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync().WaitAsync(Timeout5s));
        Assert.Contains("already running", ex.Message);
    }

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var connection = new ScriptedConnection();
        var host = RpcHost.Listen(
            new SingleConnectionServerTransport(connection), NewSerializer());

        await host.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => host.StartAsync().WaitAsync(Timeout5s));
    }

    [Fact]
    public async Task StartAsync_WhenListenerStartFails_ResetsStateAndRethrows()
    {
        var failure = new InvalidOperationException("bind failed");
        var transport = new FailingStartServerTransport(failure);
        await using var host = RpcHost.Listen(transport, NewSerializer());

        // First start surfaces the listener failure (RpcHost 98-110, 112).
        var first = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync().WaitAsync(Timeout5s));
        Assert.Same(failure, first);

        // The failed start must have reset internal state so a retry is allowed (not "already
        // running"). The second attempt fails the same way, proving the reset happened.
        var second = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync().WaitAsync(Timeout5s));
        Assert.Same(failure, second);
        Assert.Equal(2, transport.StartCalls);
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        var connection = new ScriptedConnection();
        await using var host = RpcHost.Listen(
            new SingleConnectionServerTransport(connection), NewSerializer());

        // _cts is null -> StopAsync returns a completed task without faulting (RpcHost 181-183).
        await host.StopAsync().WaitAsync(Timeout5s);
    }

    // ----- Accept -> peer creation, PeerConnected, collection.Add -----

    [Fact]
    public async Task StartAsync_AcceptsConnection_CreatesPeerAndRaisesPeerConnected()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        var acceptedPeer = await connected.Task.WaitAsync(Timeout5s);
        Assert.True(acceptedPeer.IsConnected);

        // Drive a real call through the accepted peer's read loop so the peer is genuinely live.
        var service = client.GetGameService();
        var registered = await service.RegisterPlayerAsync("p1").WaitAsync(Timeout5s);
        Assert.Equal("p1", registered.Name);
    }

    [Fact]
    public async Task MultiplePeers_AreTrackedConcurrently_AndAllDisposedOnHostStop()
    {
        const int peerCount = 4;
        var transport = new MultiConnectionServerTransport();
        var connectedPeers = new System.Collections.Concurrent.ConcurrentBag<RpcPeer>();
        var allConnected = new CountdownEvent(peerCount);

        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) =>
        {
            connectedPeers.Add(args.Peer);
            allConnected.Signal();
        };
        await host.StartAsync().WaitAsync(Timeout5s);

        var clients = new List<RpcPeer>();
        try
        {
            for (var i = 0; i < peerCount; i++)
            {
                var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
                clients.Add(RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start());
                transport.EnqueueConnection(serverConnection);
            }

            Assert.True(allConnected.Wait(Timeout10s));
            Assert.Equal(peerCount, connectedPeers.Count);
            Assert.All(connectedPeers, peer => Assert.True(peer.IsConnected));

            // Stopping the host must close every tracked peer (RpcHostPeerCollection.CloseAllAsync
            // 41-45, AwaitCleanupAsync).
            await host.StopAsync().WaitAsync(Timeout10s);
            Assert.All(connectedPeers, peer => Assert.False(peer.IsConnected));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
        allConnected.Dispose();
    }

    // ----- Peer disconnect removes from collection + raises PeerDisconnected -----

    [Fact]
    public async Task PeerDisconnect_RaisesPeerDisconnected_AndRemovesFromCollection()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        host.PeerDisconnected += (_, args) => disconnected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        var acceptedPeer = await connected.Task.WaitAsync(Timeout5s);

        // Closing the client tears down the channel; the accepted peer's read loop ends and the host's
        // OnPeerDisconnected runs: removes the peer, raises PeerDisconnected, DisposeInBackground
        // (RpcHost 302-316).
        await client.DisposeAsync();

        var disconnectedPeer = await disconnected.Task.WaitAsync(Timeout10s);
        Assert.Same(acceptedPeer, disconnectedPeer);

        // After disconnect the host should still stop cleanly (peer already removed from collection).
        await host.StopAsync().WaitAsync(Timeout10s);
    }

    // ----- Configuration failure path (RpcHost 264-269, RpcHostPeerConfiguration.Snapshot) -----

    [Fact]
    public async Task ForEachPeer_ConfigurationThrows_RaisesAcceptError_AndDoesNotRaisePeerConnected()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var failure = new InvalidOperationException("configure boom");
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerConnectedCount = 0;

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(_ => throw failure);
        host.AcceptError += (_, args) => error.TrySetResult(args.Error);
        host.PeerConnected += (_, _) => Interlocked.Increment(ref peerConnectedCount);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        Assert.Same(failure, await error.Task.WaitAsync(Timeout10s));
        Assert.Equal(0, Volatile.Read(ref peerConnectedCount));
    }

    [Fact]
    public async Task ForEachPeer_MultipleConfigurators_AllRunInRegistrationOrder()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var order = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(_ => order.Enqueue(1))
            .ForEachPeer(_ => order.Enqueue(2))
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, _) => connected.TrySetResult(true);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        Assert.True(await connected.Task.WaitAsync(Timeout10s));
        Assert.Equal(new[] { 1, 2 }, order.ToArray());
    }

    // ----- Accept loop error handling (RpcHostAcceptLoop 38-46, 107-117) -----

}
