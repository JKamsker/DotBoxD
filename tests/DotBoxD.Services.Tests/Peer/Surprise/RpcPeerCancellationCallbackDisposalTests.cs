using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerCancellationCallbackDisposalTests
{
    [Fact]
    public async Task DisposeAsync_WhenReadChannelCancellationCallbackThrows_PublishesAndCompletesOneTeardown()
    {
        var channel = new ThrowingCancellationCallbackChannel();
        var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer()).Start();
        await channel.ReceiveStarted.WaitAsync(TimeSpan.FromSeconds(1));

        var firstDispose = peer.DisposeAsync().AsTask();
        var repeatedDispose = peer.DisposeAsync().AsTask();

        Assert.Same(firstDispose, repeatedDispose);
        await Task.WhenAll(firstDispose, repeatedDispose).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, channel.DisposeCount);
    }

    private sealed class ThrowingCancellationCallbackChannel : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _receiveStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public Task ReceiveStarted => _receiveStarted.Task;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool IsConnected => DisposeCount == 0;

        public string RemoteEndpoint => "throwing-cancellation-callback://remote";

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            using var registration = ct.Register(static () => throw new InvalidOperationException("sentinel callback failure"));
            _receiveStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            return Payload.Empty;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;
    }
}
