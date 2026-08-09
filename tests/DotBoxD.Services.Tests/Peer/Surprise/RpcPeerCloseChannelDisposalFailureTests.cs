using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerCloseChannelDisposalFailureTests
{
    [Fact]
    public async Task CloseAsync_WhenChannelDisposeThrows_PropagatesFailureAfterClosingPeer()
    {
        var sentinel = new InvalidOperationException("channel dispose failed");
        var channel = new ThrowingDisposeChannel(sentinel);
        var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer()).Start();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => peer.CloseAsync());

        Assert.Same(sentinel, error);
        Assert.Equal(1, channel.DisposeCount);
        Assert.False(channel.IsConnected);
        Assert.False(peer.IsConnected);
    }

    private sealed class ThrowingDisposeChannel(InvalidOperationException failure) : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "throwing-dispose://remote";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _closed))
            {
                await _closed.Task.ConfigureAwait(false);
            }

            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            Interlocked.Exchange(ref _disposed, 1);
            _closed.TrySetResult(true);
            throw failure;
        }
    }
}
