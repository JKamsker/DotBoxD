using System.Buffers.Binary;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class RpcPeerSenderBuiltInFrameChannelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task BuiltInFastPath_StillValidatesAndDisposesMalformedFrames()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        using var sender = new RpcPeerSender(connection, static () => false);
        var frame = CreateLengthMismatchFrame();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => sender.SendFrameValueAsync(frame, CancellationToken.None).AsTask());

        AssertDisposed(frame);
    }

    [Fact]
    public async Task BuiltInFastPath_SerializesFrameAndRawSendsAtTheChannel()
    {
        await using var stream = new FirstWriteBlockingStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var sender = new RpcPeerSender(connection, static () => false);
        var frame = CreateValidFrame();

        var frameSend = sender.SendFrameValueAsync(frame, CancellationToken.None).AsTask();
        await stream.FirstWriteEntered.WaitAsync(Timeout);
        var rawSend = sender.SendAsync(CreateValidFrameBytes(), CancellationToken.None);

        Assert.False(rawSend.IsCompleted);
        stream.ReleaseFirstWrite();
        await Task.WhenAll(frameSend, rawSend).WaitAsync(Timeout);

        Assert.Equal(2, stream.WriteCount);
        AssertDisposed(frame);
    }

    [Fact]
    public async Task BuiltInFastPath_DisposesFrameWhenPeerIsAlreadyClosed()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        using var sender = new RpcPeerSender(connection, static () => true);
        var frame = CreateValidFrame();

        await Assert.ThrowsAsync<ServiceConnectionException>(
            () => sender.SendFrameValueAsync(frame, CancellationToken.None).AsTask());

        AssertDisposed(frame);
    }

    private static PooledBufferWriter CreateValidFrame()
    {
        var frame = new PooledBufferWriter();
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static byte[] CreateValidFrameBytes()
    {
        using var frame = CreateValidFrame();
        return frame.WrittenMemory.ToArray();
    }

    private static PooledBufferWriter CreateLengthMismatchFrame()
    {
        var frame = new PooledBufferWriter();
        var span = frame.GetSpan(MessageFramer.HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span, MessageFramer.MaxMessageSize + 1);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), 1);
        span[8] = (byte)MessageType.Request;
        frame.Advance(MessageFramer.HeaderSize);
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);

    private sealed class FirstWriteBlockingStream : Stream
    {
        private readonly TaskCompletionSource _firstWriteEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstWriteEntered => _firstWriteEntered.Task;

        public int WriteCount { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void ReleaseFirstWrite() => _releaseFirstWrite.TrySetResult();

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            if (WriteCount != 1)
            {
                return default;
            }

            _firstWriteEntered.TrySetResult();
            return new ValueTask(_releaseFirstWrite.Task.WaitAsync(cancellationToken));
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
