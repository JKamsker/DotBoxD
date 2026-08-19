using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Surprise;

public sealed class RpcStreamAttachmentSequentialReuseTests
{
    [Fact]
    public async Task RegisterOutbound_RejectsOwnedAttachmentAfterItsSourceWasDisposed()
    {
        var firstStreams = CreateStreams();
        var handle = firstStreams.ReserveOutbound(RpcStreamKind.Binary);
        var source = new TrackingStream();
        var attachment = RpcStreamAttachment.FromStream(handle, source, leaveOpen: false);

        await using (var firstOutbound = firstStreams.RegisterOutbound(attachment, CancellationToken.None))
        {
            firstOutbound.Start();
            await firstOutbound.WaitAsync();
        }

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.ReadCount);

        var secondStreams = CreateStreams();

        Assert.Throws<ServiceProtocolException>(
            () => secondStreams.RegisterOutbound(attachment, CancellationToken.None));

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.ReadCount);
    }

    private static RpcStreamManager CreateStreams() =>
        new(new MessagePackRpcSerializer(), SendNoopAsync, exceptionTransformer: null);

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private sealed class TrackingStream : Stream
    {
        private int _disposeCount;
        private int _readCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int ReadCount => Volatile.Read(ref _readCount);

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

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            return new ValueTask<int>(0);
        }

        public override ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
