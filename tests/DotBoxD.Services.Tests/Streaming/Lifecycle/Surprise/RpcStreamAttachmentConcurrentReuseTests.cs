using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle;

public sealed class RpcStreamAttachmentConcurrentReuseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RegisterOutbound_rejects_attachment_already_owned_by_another_manager()
    {
        var firstManager = CreateStreamManager();
        var secondManager = CreateStreamManager();
        var source = new BlockingReadStream();
        var attachment = RpcStreamAttachment.FromStream(
            firstManager.ReserveOutbound(RpcStreamKind.Binary),
            source,
            leaveOpen: false);

        await using var firstOutbound = firstManager.RegisterOutbound(attachment, CancellationToken.None);
        RpcOutboundStreamSet? secondOutbound = null;
        try
        {
            firstOutbound.Start();
            await source.ReadStarted.WaitAsync(Timeout);

            var exception = Record.Exception(
                () => secondOutbound = secondManager.RegisterOutbound(attachment, CancellationToken.None));

            Assert.IsType<ServiceProtocolException>(exception);
            Assert.Equal(1, source.ActiveReadCount);
        }
        finally
        {
            if (secondOutbound is not null)
            {
                await secondOutbound.DisposeAsync();
            }
        }
    }

    private static RpcStreamManager CreateStreamManager() =>
        new(
            new MessagePackRpcSerializer(),
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null,
            SendFrameAsync);

    private static ValueTask SendFrameAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        frame.Dispose();
        return default;
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _readReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeReadCount;

        public int ActiveReadCount => Volatile.Read(ref _activeReadCount);

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

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _activeReadCount);
            _readStarted.TrySetResult();
            try
            {
                return await _readReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeReadCount);
            }
        }

        public override ValueTask DisposeAsync()
        {
            _readReleased.TrySetResult(0);
            return default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _readReleased.TrySetResult(0);
            }

            base.Dispose(disposing);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
