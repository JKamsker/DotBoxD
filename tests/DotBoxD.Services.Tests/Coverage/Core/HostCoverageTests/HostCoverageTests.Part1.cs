using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class HostCoverageTests
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

    // ----------------------------------------------------------------------------------------
    // Test transports
    // ----------------------------------------------------------------------------------------

    /// <summary>Server transport whose <c>StartAsync</c> always throws, to drive the host's
    /// listener-start failure path.</summary>
    private sealed class FailingStartServerTransport : IServerTransport
    {
        private readonly Exception _failure;
        private int _startCalls;

        public FailingStartServerTransport(Exception failure) => _failure = failure;

        public int StartCalls => Volatile.Read(ref _startCalls);

        public Task StartAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _startCalls);
            throw _failure;
        }

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Accept should never run when start fails.");

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Server transport that hands back queued connections one per accept, then parks until
    /// stopped/cancelled — lets the host accept several peers concurrently.</summary>
    private sealed class MultiConnectionServerTransport : IServerTransport
    {
        private readonly System.Threading.Channels.Channel<IRpcChannel> _connections =
            System.Threading.Channels.Channel.CreateUnbounded<IRpcChannel>(
                new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        private int _disposed;

        public void EnqueueConnection(IRpcChannel connection) => _connections.Writer.TryWrite(connection);

        public Task StartAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(MultiConnectionServerTransport));
            }

            return Task.CompletedTask;
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            try
            {
                return await _connections.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
                throw new OperationCanceledException(ct);
            }
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _connections.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _connections.Writer.TryComplete();
            return default;
        }
    }

    /// <summary>Accept fails the first <c>faultCount</c> times (driving the loop's error/backoff
    /// path), then returns one real connection, then parks until cancelled.</summary>
    private sealed class FaultThenAcceptServerTransport : IServerTransport
    {
        private readonly int _faultCount;
        private readonly IRpcChannel _connection;
        private int _acceptCalls;
        private int _delivered;

        public FaultThenAcceptServerTransport(int faultCount, IRpcChannel connection)
        {
            _faultCount = faultCount;
            _connection = connection;
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _acceptCalls);
            if (call <= _faultCount)
            {
                throw new InvalidOperationException($"transient accept failure #{call}");
            }

            if (Interlocked.Exchange(ref _delivered, 1) == 0)
            {
                return _connection;
            }

            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Single-connection server transport that records whether the host disposed it.</summary>
    private sealed class DisposeTrackingSingleConnectionTransport : IServerTransport
    {
        private readonly IRpcChannel _connection;
        private readonly TaskCompletionSource<bool> _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _accepted;
        private int _started;
        private int _disposed;

        public DisposeTrackingSingleConnectionTransport(IRpcChannel connection) => _connection = connection;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public Task StartAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _started, 1);
            return Task.CompletedTask;
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _started) == 0)
            {
                throw new InvalidOperationException("Transport not started.");
            }

            if (Interlocked.Exchange(ref _accepted, 1) == 0)
            {
                return _connection;
            }

            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _stopped))
            {
                await _stopped.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _stopped.TrySetResult(true);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _stopped.TrySetResult(true);
            return default;
        }
    }

}
