using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerStreamArrayNullContractTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static TheoryData<string, Func<RpcPeer, Task>> RpcPeerNullStreamArrayCalls() =>
        new()
        {
            {
                "RpcPeer request-response",
                peer => peer.InvokeAsync<int, int>("Missing", "Echo", 1, (RpcStreamAttachment[])null!)
            },
            {
                "RpcPeer request-no-response",
                peer => peer.InvokeAsync<int>("Missing", "Notify", 1, (RpcStreamAttachment[])null!)
            },
            {
                "RpcPeer instance request-response",
                peer => peer.InvokeOnInstanceAsync<int, int>(
                    "Missing",
                    "instance-1",
                    "Echo",
                    1,
                    (RpcStreamAttachment[])null!)
            },
            {
                "RpcPeer instance request-no-response",
                peer => peer.InvokeOnInstanceAsync<int>(
                    "Missing",
                    "instance-1",
                    "Notify",
                    1,
                    (RpcStreamAttachment[])null!)
            },
        };

    public static TheoryData<string, Func<IRpcInvoker, Task>> InvokerNullStreamArrayCalls() =>
        new()
        {
            {
                "IRpcInvoker request-response",
                invoker => invoker.InvokeAsync<int, int>("Missing", "Echo", 1, (RpcStreamAttachment[])null!)
            },
            {
                "IRpcInvoker request-no-response",
                invoker => invoker.InvokeAsync<int>("Missing", "Notify", 1, (RpcStreamAttachment[])null!)
            },
            {
                "IRpcInvoker instance request-response",
                invoker => invoker.InvokeOnInstanceAsync<int, int>(
                    "Missing",
                    "instance-1",
                    "Echo",
                    1,
                    (RpcStreamAttachment[])null!)
            },
            {
                "IRpcInvoker instance request-no-response",
                invoker => invoker.InvokeOnInstanceAsync<int>(
                    "Missing",
                    "instance-1",
                    "Notify",
                    1,
                    (RpcStreamAttachment[])null!)
            },
        };

    [Theory]
    [MemberData(nameof(RpcPeerNullStreamArrayCalls))]
    public async Task RpcPeer_stream_array_overloads_reject_null_before_sending(
        string scenario,
        Func<RpcPeer, Task> invoke)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        await using var pair = StartPeerPairWithoutServices();
        await AssertRejectsNullStreamsAsync(pair, () => invoke(pair.Client));
    }

    [Theory]
    [MemberData(nameof(InvokerNullStreamArrayCalls))]
    public async Task IRpcInvoker_stream_array_overloads_reject_null_before_sending(
        string scenario,
        Func<IRpcInvoker, Task> invoke)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        await using var pair = StartPeerPairWithoutServices();
        await AssertRejectsNullStreamsAsync(pair, () => invoke((IRpcInvoker)pair.Client));
    }

    private static async Task AssertRejectsNullStreamsAsync(PeerPair pair, Func<Task> invoke)
    {
        var ex = await Record.ExceptionAsync(() => invoke().WaitAsync(Timeout));

        var argument = Assert.IsType<ArgumentNullException>(ex);
        Assert.Equal("streams", argument.ParamName);
        Assert.Equal(0, pair.ClientChannel.SendCount);
    }

    private static PeerPair StartPeerPairWithoutServices()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        var clientChannel = new CountingChannel(clientConnection);

        var client = RpcPeer
            .Over(clientChannel, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .Start();
        var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .Start();

        return new PeerPair(client, server, clientChannel);
    }

    private sealed class PeerPair : IAsyncDisposable
    {
        public PeerPair(RpcPeer client, RpcPeer server, CountingChannel clientChannel)
        {
            Client = client;
            Server = server;
            ClientChannel = clientChannel;
        }

        public RpcPeer Client { get; }

        public RpcPeer Server { get; }

        public CountingChannel ClientChannel { get; }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
            await Server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class CountingChannel : IRpcChannel
    {
        private readonly IRpcChannel _inner;
        private int _sendCount;

        public CountingChannel(IRpcChannel inner) => _inner = inner;

        public bool IsConnected => _inner.IsConnected;

        public string RemoteEndpoint => _inner.RemoteEndpoint;

        public int SendCount => Volatile.Read(ref _sendCount);

        public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _sendCount);
            await _inner.SendAsync(data, ct).ConfigureAwait(false);
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            _inner.ReceiveAsync(ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
