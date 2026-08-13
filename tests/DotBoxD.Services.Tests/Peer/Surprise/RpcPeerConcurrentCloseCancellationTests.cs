using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerConcurrentCloseCancellationTests
{
    [Fact]
    public async Task CloseAsync_PreCanceledSecondCaller_AwaitsPublishedTeardownFailure()
    {
        var channel = new GatedFaultingDisposeChannel();
        var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer()).Start();

        var firstClose = peer.CloseAsync();
        await channel.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(1));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var secondClose = peer.CloseAsync(cancellation.Token);

        Assert.False(secondClose.IsCompleted);

        channel.ReleaseDispose();

        var firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => firstClose);
        var secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => secondClose);
        Assert.Same(firstFailure, secondFailure);
    }

    private sealed class GatedFaultingDisposeChannel : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _disposeEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseDispose =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly InvalidOperationException _disposeFailure = new("Sentinel teardown failure.");
        private int _disposed;

        public Task DisposeEntered => _disposeEntered.Task;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "gated-faulting://remote";

        public void ReleaseDispose() => _releaseDispose.TrySetResult(true);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            var parked = new TaskCompletionSource<Payload>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<Payload>)state!).TrySetResult(Payload.Empty), parked))
            {
                return await parked.Task.ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _disposeEntered.TrySetResult(true);
            await _releaseDispose.Task.ConfigureAwait(false);
            throw _disposeFailure;
        }
    }
}
