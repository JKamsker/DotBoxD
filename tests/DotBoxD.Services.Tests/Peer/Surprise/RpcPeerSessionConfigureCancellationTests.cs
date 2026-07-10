using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer;

public sealed class RpcPeerSessionConfigureCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ConnectPeerAsync_WhenConfigureCancelsTokenDisposesTransportAndDoesNotStartPeer()
    {
        var serializer = new MessagePackRpcSerializer();
        var channel = new TrackingChannel();
        var transport = new TrackingTransport(channel);
        using var cts = new CancellationTokenSource();
        RpcPeer? configuredPeer = null;

        RpcPeerSession? session = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            session = await transport.ConnectPeerAsync(
                serializer,
                peer =>
                {
                    configuredPeer = peer;
                    cts.Cancel();
                },
                new RpcPeerOptions { RequestTimeout = Timeout },
                cts.Token);
        });

        var disposedBeforeCleanup = transport.Disposed;
        var channelConnectedBeforeCleanup = channel.IsConnected;
        var peerStartedBeforeCleanup = configuredPeer?.HasStarted;
        var peerDisposedBeforeCleanup = configuredPeer?.IsDisposed;

        if (session is not null)
        {
            await session.DisposeAsync();
        }

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(transport.ConnectCalled);
        Assert.True(disposedBeforeCleanup);
        Assert.False(channelConnectedBeforeCleanup);
        Assert.NotNull(configuredPeer);
        Assert.False(peerStartedBeforeCleanup);
        Assert.True(peerDisposedBeforeCleanup);
        Assert.False(channel.ReceiveCalled);
        Assert.Null(session);
    }

    private sealed class TrackingTransport : ITransport
    {
        private readonly IRpcChannel _connection;

        public TrackingTransport(IRpcChannel connection) => _connection = connection;

        public bool ConnectCalled { get; private set; }

        public bool Disposed { get; private set; }

        public IRpcChannel? Connection { get; private set; }

        public bool IsConnected => !Disposed && Connection?.IsConnected == true;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ConnectCalled = true;
            Connection = _connection;
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            Disposed = true;
            await _connection.DisposeAsync();
        }
    }

    private sealed class TrackingChannel : IRpcChannel
    {
        public bool ReceiveCalled { get; private set; }

        public bool IsConnected { get; private set; } = true;

        public string RemoteEndpoint => "memory://configure-cancel";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            ReceiveCalled = true;
            return Task.FromResult(Payload.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
