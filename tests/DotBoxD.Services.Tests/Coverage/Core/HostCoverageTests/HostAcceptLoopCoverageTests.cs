using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.HostCoverageTestSupport;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class HostAcceptLoopCoverageTests
{
    [Fact]
    public async Task AcceptAsync_TransientErrors_RaiseAcceptError_AndLoopKeepsAccepting()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var faultCount = 2;
        var transport = new FaultThenAcceptServerTransport(faultCount, serverConnection);
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var errorsSeen = new CountdownEvent(faultCount);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.AcceptError += (_, args) =>
        {
            errors.Add(args.Error);
            if (errorsSeen.CurrentCount > 0)
            {
                errorsSeen.Signal();
            }
        };
        host.PeerConnected += (_, _) => connected.TrySetResult(true);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        // The loop reported each transient accept failure as an AcceptError ...
        Assert.True(errorsSeen.Wait(Timeout10s));
        // ... and after backing off kept looping, eventually accepting the real connection.
        Assert.True(await connected.Task.WaitAsync(Timeout10s));
        Assert.All(errors, ex => Assert.IsType<InvalidOperationException>(ex));
        errorsSeen.Dispose();
    }

    [Fact]
    public async Task RaiseAcceptError_WithNoHandler_DoesNotFaultTheLoop()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var transport = new FaultThenAcceptServerTransport(faultCount: 1, serverConnection);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // No AcceptError subscriber: the loop must still continue past the fault and accept.
        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, _) => connected.TrySetResult(true);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        Assert.True(await connected.Task.WaitAsync(Timeout10s));
    }

    // ----- DisposeAsync stops the accept loop and disposes peers -----

    [Fact]
    public async Task DisposeAsync_StopsAcceptLoop_DisposesPeers_AndDisposesListener()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var transport = new DisposeTrackingSingleConnectionTransport(serverConnection);
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();
        var acceptedPeer = await connected.Task.WaitAsync(Timeout5s);
        Assert.True(acceptedPeer.IsConnected);

        await host.DisposeAsync().AsTask().WaitAsync(Timeout10s);

        Assert.False(acceptedPeer.IsConnected);
        Assert.True(transport.IsDisposed);

        // DisposeAsync is idempotent: a second call returns without faulting (RpcHost 320-322).
        await host.DisposeAsync().AsTask().WaitAsync(Timeout5s);
    }

    [Fact]
    public async Task DisposeAsync_OnNeverStartedHost_DisposesListenerWithoutFault()
    {
        var connection = new ScriptedConnection();
        var transport = new DisposeTrackingSingleConnectionTransport(connection);
        var host = RpcHost.Listen(transport, NewSerializer());

        await host.DisposeAsync().AsTask().WaitAsync(Timeout5s);

        Assert.True(transport.IsDisposed);
    }

}
