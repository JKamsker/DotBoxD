using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.PeerDisposal;

public sealed class RpcPeerDisposeOutboundStreamCallbackDeadlockTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task DisposeAsync_CompletesAndReleasesOwnedSource_when_outbound_callback_awaits_disposal()
    {
        var source = new DisposeJoiningStream();
        var (left, _) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        var client = RpcPeer.Over(left, serializer).Start();
        Task? dispose = null;
        try
        {
            var handle = client.ReserveStream(RpcStreamKind.Binary);
            _ = client.InvokeAsync<RpcStreamHandle, int>(
                "Teardown",
                "Wait",
                handle,
                RpcStreamAttachment.FromStream(handle, source, leaveOpen: false));

            await source.ReadStarted.WaitAsync(Timeout);

            dispose = Task.Run(async () => await client.DisposeAsync());
            source.SetDisposal(dispose);
            await source.CallbackEntered.WaitAsync(Timeout);

            await dispose.WaitAsync(Timeout);
            await source.Disposed.WaitAsync(Timeout);
            Assert.Equal(1, source.DisposeCount);
        }
        finally
        {
            source.AllowCallbackToReturn();
            if (dispose is not null)
            {
                _ = await Record.ExceptionAsync(() => dispose.WaitAsync(Timeout));
            }
            else
            {
                _ = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask().WaitAsync(Timeout));
            }

        }
    }

    private sealed class DisposeJoiningStream : Stream
    {
        private readonly TaskCompletionSource _callbackEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _callbackMayReturn =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _read =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Task> _disposal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _cancellationRegistration;
        private int _disposeCount;

        public Task CallbackEntered => _callbackEntered.Task;

        public Task Disposed => _disposed.Task;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task ReadStarted => _readStarted.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void AllowCallbackToReturn()
        {
            _callbackMayReturn.TrySetResult();
            _disposal.TrySetResult(Task.CompletedTask);
        }

        public void SetDisposal(Task disposal) => _disposal.TrySetResult(disposal);

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _cancellationRegistration = cancellationToken.Register(OnCanceled);
            _readStarted.TrySetResult();
            return new ValueTask<int>(_read.Task);
        }

        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                _cancellationRegistration.Dispose();
                _read.TrySetResult(0);
                _disposed.TrySetResult();
            }

            return default;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private void OnCanceled()
        {
            _callbackEntered.TrySetResult();
            var disposal = _disposal.Task.GetAwaiter().GetResult();
            _ = Task.WaitAny(disposal, _callbackMayReturn.Task);
        }
    }
}
