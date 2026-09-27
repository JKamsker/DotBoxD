using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Registration;

public sealed class RpcAttachmentPendingClaimPeerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Canceled_call_cannot_reuse_attachment_while_its_source_read_is_pending(bool batch, bool otherPeer)
    {
        await using var firstPeer = RpcPeer.Over(new PendingChannel(), new MessagePackRpcSerializer()).Start();
        await using var secondPeer = RpcPeer.Over(new PendingChannel(), new MessagePackRpcSerializer()).Start();
        await using var source = new PendingStream();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var handle = firstPeer.ReserveStream(RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromStream(handle, source);
        Task<int>? second = null;
        try
        {
            var first = Invoke(firstPeer, firstCancellation.Token);
            await source.FirstStarted.Task.WaitAsync(Timeout);
            firstCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Timeout));
            Assert.False(source.ReadFinished.Task.IsCompleted);

            second = Invoke(otherPeer ? secondPeer : firstPeer, secondCancellation.Token);
            await Task.WhenAny(source.SecondStarted.Task, second).WaitAsync(Timeout);

            Assert.True(second.IsCompleted, "The second call started another read of the pending source.");
            await Assert.ThrowsAsync<ServiceProtocolException>(() => second);
            Assert.Equal(1, source.ReadCount);
        }
        finally
        {
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            source.Release.TrySetResult();
            if (second is not null)
            {
                _ = await Record.ExceptionAsync(() => second.WaitAsync(Timeout));
            }
            if (source.FirstStarted.Task.IsCompleted)
            {
                await source.ReadFinished.Task.WaitAsync(Timeout);
            }
        }

        Task<int> Invoke(RpcPeer peer, CancellationToken ct) => batch
            ? peer.InvokeAsync<RpcStreamHandle, int>("Receiver", "Read", handle, [attachment], ct)
            : peer.InvokeAsync<RpcStreamHandle, int>("Receiver", "Read", handle, attachment, ct);
    }

    private sealed class PendingStream : MemoryStream
    {
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var first = Interlocked.Increment(ref _readCount) == 1;
            (first ? FirstStarted : SecondStarted).TrySetResult();
            await Release.Task.ConfigureAwait(false);
            if (first)
            {
                ReadFinished.TrySetResult();
            }
            return 0;
        }
    }

    private sealed class PendingChannel : IRpcChannel
    {
        private readonly CancellationTokenSource _closed = new();
        private int _disposed;
        public bool IsConnected => Volatile.Read(ref _disposed) == 0;
        public string RemoteEndpoint => "local://pending";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closed.Token);
            await Task.Delay(System.Threading.Timeout.Infinite, linked.Token).ConfigureAwait(false);
            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _closed.Cancel();
                _closed.Dispose();
            }
            return default;
        }
    }
}
