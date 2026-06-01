using System.Threading.Channels;
using System.Net;
using Shared;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// End-to-end coverage for the symmetric <see cref="RpcPeer"/> model over an in-memory
/// duplex channel: one-directional calls, bidirectional calls on a single connection, and
/// the explicit reject-inbound behaviour.
/// </summary>
public class PeerTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task OneDirectional_GetOnlyPeer_CallsProviderAndGetsData()
    {
        var (clientChannel, serverChannel) = InMemoryChannel.CreatePair();

        await using var provider = RpcPeer.Over(serverChannel, NewSerializer())
            .Provide<IGameService>(new TestGameService())
            .Start();

        await using var caller = RpcPeer.Over(clientChannel, NewSerializer(),
                new RpcPeerOptions { RejectInboundCalls = true })
            .Start();

        var game = caller.GetGameService();
        var status = await game.GetServerStatusAsync();

        Assert.NotNull(status);

        var player = await game.RegisterPlayerAsync("Peer");
        Assert.Equal("Peer", player.Name);
    }

    [Fact]
    public async Task Bidirectional_BothSidesProvideAndCall_OverOneConnection()
    {
        var (channelA, channelB) = InMemoryChannel.CreatePair();

        // Side A provides the game; side B provides player notifications.
        await using var a = RpcPeer.Over(channelA, NewSerializer())
            .Provide<IGameService>(new TestGameService())
            .Start();

        var notifications = new RecordingNotifications("client-42");
        await using var b = RpcPeer.Over(channelB, NewSerializer())
            .Provide<IPlayerNotifications>(notifications)
            .Start();

        // B -> A : call the game service.
        var game = b.GetGameService();
        var registered = await game.RegisterPlayerAsync("Hero");
        Assert.Equal("Hero", registered.Name);

        // A -> B : call back into the connecting peer over the SAME connection.
        var callback = a.GetPlayerNotifications();
        await callback.NotifyAsync("level-up");
        var who = await callback.WhoAmIAsync();

        Assert.Equal("client-42", who);
        Assert.Equal("level-up", Assert.Single(notifications.Messages));
    }

    [Fact]
    public async Task RejectInboundCalls_ProducesRemoteError_WhenOtherSideCallsBack()
    {
        var (channelA, channelB) = InMemoryChannel.CreatePair();

        // A provides the game AND tries to call back into B.
        await using var a = RpcPeer.Over(channelA, NewSerializer())
            .Provide<IGameService>(new TestGameService())
            .Start();

        // B refuses inbound calls but still calls out.
        await using var b = RpcPeer.Over(channelB, NewSerializer(),
                new RpcPeerOptions { RejectInboundCalls = true, RequestTimeout = TimeSpan.FromSeconds(5) })
            .Provide<IPlayerNotifications>(new RecordingNotifications("nope"))
            .Start();

        // Outbound call from B still works.
        var game = b.GetGameService();
        Assert.NotNull(await game.GetServerStatusAsync());

        // Inbound call from A is rejected by B.
        var callback = a.GetPlayerNotifications();
        var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(() => callback.WhoAmIAsync());
        Assert.Contains("does not accept inbound calls", ex.Message);
    }

    [Fact]
    public async Task RpcHost_AcceptsConnections_AndCallsBackIntoConnectingPeer()
    {
        var (clientChannel, serverChannel) = InMemoryChannel.CreatePair();

        RpcPeer? hostPeer = null;
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverChannel), NewSerializer())
            .ForEachPeer(peer => peer.Provide<IGameService>(new TestGameService()));
        host.PeerConnected += (_, args) =>
        {
            hostPeer = args.Peer;
            connected.TrySetResult(args.Peer);
        };
        await host.StartAsync();

        var notifications = new RecordingNotifications("host-client");
        await using var client = RpcPeer.Over(clientChannel, NewSerializer())
            .Provide<IPlayerNotifications>(notifications)
            .Start();

        var game = client.GetGameService();
        Assert.NotNull(await game.GetServerStatusAsync());

        var accepted = await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var callback = accepted.GetPlayerNotifications();
        await callback.NotifyAsync("hello from host");

        Assert.Equal("hello from host", Assert.Single(notifications.Messages));
    }

    [Fact]
    public async Task RpcPeer_OverTcp_RoundTrips()
    {
        Exception? clientReadError = null;
        Exception? serverReadError = null;
        var serverTransport = new ShaRPC.Transports.Tcp.TcpServerTransport(IPAddress.Loopback, 0);

        await using var host = RpcHost
            .Listen(serverTransport, NewSerializer())
            .ForEachPeer(peer =>
            {
                peer.Provide<IGameService>(new TestGameService());
                peer.ReadError += (_, args) => serverReadError = args.Error;
            });
        await host.StartAsync();
        var port = serverTransport.LocalEndpoint?.Port ??
            throw new InvalidOperationException("TCP test server did not expose a bound port.");

        var transport = new ShaRPC.Transports.Tcp.TcpTransport("127.0.0.1", port);
        await transport.ConnectAsync();
        await using var client = RpcPeer.Over(transport.Connection!, NewSerializer(),
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        client.ReadError += (_, args) => clientReadError = args.Error;
        client.Start();

        var game = client.GetGameService();
        try
        {
            var status = await game.GetServerStatusAsync();
            Assert.NotNull(status);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"call failed: {ex.Message}; clientReadError={clientReadError}; serverReadError={serverReadError}");
        }
    }

    [Fact]
    public async Task RpcPeer_OverTcp_Bidirectional_LikeSample()
    {
        var greeted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTransport = new ShaRPC.Transports.Tcp.TcpServerTransport(IPAddress.Loopback, 0);

        // Exercise the generated Provide/Get extension methods (the shape the sample uses).
        await using var host = RpcHost
            .Listen(serverTransport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => _ = GreetAsync(args.Peer, greeted);
        await host.StartAsync();
        var port = serverTransport.LocalEndpoint?.Port ??
            throw new InvalidOperationException("TCP test server did not expose a bound port.");

        var transport = new ShaRPC.Transports.Tcp.TcpTransport("127.0.0.1", port);
        await transport.ConnectAsync();
        await using var client = RpcPeer.Over(transport.Connection!, NewSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .ProvidePlayerNotifications(new RecordingNotifications("sample-client"))
            .Start();

        var game = client.GetGameService();
        var status = await game.GetServerStatusAsync();
        Assert.NotNull(status);

        var who = await greeted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("sample-client", who);

        static async Task GreetAsync(RpcPeer peer, TaskCompletionSource<string> done)
        {
            try
            {
                var notifications = peer.GetPlayerNotifications();
                var who = await notifications.WhoAmIAsync();
                await notifications.NotifyAsync($"Welcome, {who}!");
                done.TrySetResult(who);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }
    }

    private sealed class RecordingNotifications : IPlayerNotifications
    {
        private readonly string _identity;
        public List<string> Messages { get; } = new();

        public RecordingNotifications(string identity) => _identity = identity;

        public Task NotifyAsync(string message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<string> WhoAmIAsync(CancellationToken ct = default) => Task.FromResult(_identity);
    }

    /// <summary>An in-process, full-duplex <see cref="IConnection"/> pair backed by two channels.</summary>
    private sealed class InMemoryChannel : IConnection
    {
        private readonly ChannelReader<byte[]> _inbound;
        private readonly ChannelWriter<byte[]> _outbound;
        private readonly string _name;
        private int _disposed;

        private InMemoryChannel(ChannelReader<byte[]> inbound, ChannelWriter<byte[]> outbound, string name)
        {
            _inbound = inbound;
            _outbound = outbound;
            _name = name;
        }

        public static (IConnection A, IConnection B) CreatePair()
        {
            var ab = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
            var ba = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
            var a = new InMemoryChannel(ba.Reader, ab.Writer, "peer-a");
            var b = new InMemoryChannel(ab.Reader, ba.Writer, "peer-b");
            return (a, b);
        }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => _name;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            _outbound.TryWrite(data.ToArray());
            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                var bytes = await _inbound.ReadAsync(ct).ConfigureAwait(false);
                var payload = Payload.Rent(bytes.Length);
                bytes.CopyTo(payload.Memory);
                return payload;
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _outbound.TryComplete();
            }

            return default;
        }
    }
}
