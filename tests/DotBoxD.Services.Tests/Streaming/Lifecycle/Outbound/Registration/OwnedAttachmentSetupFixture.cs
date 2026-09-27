using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Registration;

internal sealed class OwnedAttachmentSetupFixture : IAsyncDisposable
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _ownerCancellation = new();
    private readonly bool _batch;
    private Task<int>? _owner;

    internal OwnedAttachmentSetupFixture(bool batch, bool capacityLimit = false)
    {
        _batch = batch;
        Peer = RpcPeer.Over(new PendingChannel(), new MessagePackRpcSerializer(),
            new RpcPeerOptions { MaxPendingRequests = capacityLimit ? 1 : 10 }).Start();
        Attachment = RpcStreamAttachment.FromStream(Peer.ReserveStream(RpcStreamKind.Binary), Source, leaveOpen: false);
    }

    internal RpcPeer Peer { get; }
    internal TrackingStream Source { get; } = new();
    internal RpcStreamAttachment Attachment { get; }

    internal async Task StartOwnerAsync()
    {
        _owner = InvokeAsync(Attachment, _ownerCancellation.Token);
        await Source.Started.Task.WaitAsync(Timeout);
    }

    internal Task<int> InvokeAsync(RpcStreamAttachment attachment, CancellationToken ct = default, string service = "Receiver") => _batch
        ? Peer.InvokeAsync<RpcStreamHandle, int>(service, "Read", attachment.Handle, [attachment], ct)
        : Peer.InvokeAsync<RpcStreamHandle, int>(service, "Read", attachment.Handle, attachment, ct);

    public async ValueTask DisposeAsync()
    {
        _ownerCancellation.Cancel();
        Source.Release.TrySetResult();
        if (_owner is not null)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _owner.WaitAsync(Timeout));
            await Source.Finished.Task.WaitAsync(Timeout);
        }
        await Peer.DisposeAsync();
        _ownerCancellation.Dispose();
        await Source.DisposeAsync();
    }

    internal sealed class TrackingStream : MemoryStream
    {
        private int _disposals;
        internal int Disposals => Volatile.Read(ref _disposals);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            Finished.TrySetResult();
            return 0;
        }

        public override ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposals);
            return base.DisposeAsync();
        }
    }

    private sealed class PendingChannel : IRpcChannel
    {
        private readonly CancellationTokenSource _closed = new();
        private int _disposed;
        public bool IsConnected => Volatile.Read(ref _disposed) == 0;
        public string RemoteEndpoint => "local://setup-ownership";

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
