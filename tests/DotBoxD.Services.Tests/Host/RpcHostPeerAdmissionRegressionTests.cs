using System.Threading.Channels;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Peer;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostPeerAdmissionRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task MaxAcceptedPeers_DisposesConnectionsBeyondLimitBeforePeerConnected()
    {
        var (clientA, serverA) = InMemoryPipe.CreateConnectionPair();
        var (clientB, serverB) = InMemoryPipe.CreateConnectionPair();
        var connectedCount = 0;
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost.Listen(
            new MultiConnectionServerTransport(new IRpcChannel[] { serverA, serverB }),
            NewSerializer(),
            new RpcPeerOptions { MaxAcceptedPeers = 1 });
        host.PeerConnected += (_, _) =>
        {
            if (Interlocked.Increment(ref connectedCount) == 1)
            {
                connected.TrySetResult();
            }
        };

        try
        {
            await host.StartAsync();
            await connected.Task.WaitAsync(Timeout);
            await WaitForAsync(() => !serverA.IsConnected || !serverB.IsConnected);

            Assert.Equal(1, Volatile.Read(ref connectedCount));
            Assert.True(serverA.IsConnected ^ serverB.IsConnected);
        }
        finally
        {
            await clientA.DisposeAsync();
            await clientB.DisposeAsync();
        }
    }

    [Fact]
    public async Task MaxAcceptedPeers_ReleasesSlotAfterPeerDisconnects()
    {
        var transport = new QueuedServerTransport();
        var connectedCount = 0;
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost.Listen(
            transport,
            NewSerializer(),
            new RpcPeerOptions { MaxAcceptedPeers = 1 });
        host.PeerConnected += (_, _) => Interlocked.Increment(ref connectedCount);
        host.PeerDisconnected += (_, _) => disconnected.TrySetResult();
        await host.StartAsync();

        var (clientA, serverA) = InMemoryPipe.CreateConnectionPair();
        await transport.EnqueueAsync(serverA);
        await WaitForAsync(() => Volatile.Read(ref connectedCount) == 1);

        await clientA.DisposeAsync();
        await disconnected.Task.WaitAsync(Timeout);

        var (clientB, serverB) = InMemoryPipe.CreateConnectionPair();
        await transport.EnqueueAsync(serverB);
        await WaitForAsync(() => Volatile.Read(ref connectedCount) == 2);

        await clientB.DisposeAsync();
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!predicate())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    private static MessagePackRpcSerializer NewSerializer() => new();

    private sealed class QueuedServerTransport : IServerTransport
    {
        private readonly Channel<IRpcChannel> _connections = Channel.CreateUnbounded<IRpcChannel>();

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            await _connections.Reader.ReadAsync(ct).ConfigureAwait(false);

        public Task StopAsync(CancellationToken ct = default)
        {
            _connections.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _connections.Writer.TryComplete();
            return default;
        }

        public async Task EnqueueAsync(IRpcChannel channel) =>
            await _connections.Writer.WriteAsync(channel).ConfigureAwait(false);
    }
}
