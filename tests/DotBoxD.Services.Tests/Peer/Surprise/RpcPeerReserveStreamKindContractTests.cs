using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerReserveStreamKindContractTests
{
    [Fact]
    public async Task RpcPeer_ReserveStream_rejects_undefined_kind_before_reserving()
    {
        await using var pair = StartPeerPairWithoutServices();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => pair.Client.ReserveStream((RpcStreamKind)99));

        Assert.Equal("kind", ex.ParamName);
        Assert.Equal(0, pair.ClientChannel.SendCount);

        var valid = pair.Client.ReserveStream(RpcStreamKind.Binary);
        Assert.Equal(1, valid.StreamId);
        pair.Client.ReleaseStream(valid);
    }

    [Fact]
    public async Task IRpcInvoker_ReserveStream_rejects_undefined_kind_before_reserving()
    {
        await using var pair = StartPeerPairWithoutServices();
        var invoker = (IRpcInvoker)pair.Client;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => invoker.ReserveStream((RpcStreamKind)99));

        Assert.Equal("kind", ex.ParamName);
        Assert.Equal(0, pair.ClientChannel.SendCount);

        var valid = invoker.ReserveStream(RpcStreamKind.Binary);
        Assert.Equal(1, valid.StreamId);
        invoker.ReleaseStream(valid);
    }

    private static PeerPair StartPeerPairWithoutServices()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        var clientChannel = new CountingChannel(clientConnection);

        var client = RpcPeer.Over(clientChannel, serializer).Start();
        var server = RpcPeer.Over(serverConnection, serializer).Start();

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
