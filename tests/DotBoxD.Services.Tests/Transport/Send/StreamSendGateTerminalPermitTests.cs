using System.Buffers.Binary;
using System.IO.Pipes;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class StreamSendGateTerminalPermitTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeAsync_WakesEveryQueuedSendAndDisposesOwnedFrameBeforeCompletion()
    {
        await using var stream = new NoWriteStream();
        var connection = new StreamConnection(stream, ownsStream: false);
        var rawFrame = CreateRawFrame();
        Assert.True(connection.SendGate.Wait(0));
        var ownedFrame = CreateOwnedFrame();
        var rawSends = Enumerable.Range(0, 4)
            .Select(_ => connection.SendValueAsync(rawFrame).AsTask())
            .ToArray();
        var ownedSend = connection.SendFrameValueAsync(ownedFrame).AsTask();

        try
        {
            Assert.All(rawSends, static send => Assert.False(send.IsCompleted));
            Assert.False(ownedSend.IsCompleted);
            _ = ownedFrame.WrittenMemory;

            await connection.DisposeAsync();

            foreach (var rawSend in rawSends)
            {
                var disposed = await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => rawSend.WaitAsync(TestTimeout));
                Assert.Equal(nameof(StreamConnection), disposed.ObjectName);
            }

            var ownedDisposed = await Assert.ThrowsAsync<ObjectDisposedException>(
                () => ownedSend.WaitAsync(TestTimeout));
            Assert.Equal(nameof(StreamConnection), ownedDisposed.ObjectName);
            AssertDisposed(ownedFrame);
            Assert.Equal(0, stream.WriteCalls);
            Assert.Equal(0, stream.DisposeCalls);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => connection.SendValueAsync(rawFrame).AsTask());
        }
        finally
        {
            connection.ReleaseSendGate();
            Assert.Equal(1, connection.SendGate.CurrentCount);
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task CallerCancellationBeforeDispose_RemainsCancellationAndLeavesTerminalPermitReusable()
    {
        await using var stream = new NoWriteStream();
        var connection = new StreamConnection(stream, ownsStream: false);
        var rawFrame = CreateRawFrame();
        Assert.True(connection.SendGate.Wait(0));
        using var cancellation = new CancellationTokenSource();
        var ownedFrame = CreateOwnedFrame();
        var rawSend = connection.SendValueAsync(rawFrame, cancellation.Token).AsTask();
        var ownedSend = connection.SendFrameValueAsync(ownedFrame, cancellation.Token).AsTask();

        try
        {
            Assert.False(rawSend.IsCompleted);
            Assert.False(ownedSend.IsCompleted);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => rawSend.WaitAsync(TestTimeout));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ownedSend.WaitAsync(TestTimeout));
            AssertDisposed(ownedFrame);
            Assert.Equal(0, connection.SendGate.CurrentCount);

            await connection.DisposeAsync();
            Assert.Equal(1, connection.SendGate.CurrentCount);
            Assert.Equal(0, stream.WriteCalls);
        }
        finally
        {
            connection.ReleaseSendGate();
            Assert.Equal(1, connection.SendGate.CurrentCount);
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeRacingOriginalOwnerRelease_LeavesOnePermitAndPerformsNoWrite()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            await using var stream = new NoWriteStream();
            var connection = new StreamConnection(stream, ownsStream: false);
            Assert.True(connection.SendGate.Wait(0));
            var ownedFrame = CreateOwnedFrame();
            var ownedSend = connection.SendFrameValueAsync(ownedFrame).AsTask();
            var rawSend = connection.SendValueAsync(CreateRawFrame()).AsTask();

            var close = connection.DisposeAsync().AsTask();
            connection.ReleaseSendGate();
            await close.WaitAsync(TestTimeout);

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => ownedSend.WaitAsync(TestTimeout));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => rawSend.WaitAsync(TestTimeout));
            AssertDisposed(ownedFrame);
            Assert.Equal(1, connection.SendGate.CurrentCount);
            Assert.Equal(0, stream.WriteCalls);
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task NamedPipeDispose_WakesRawAndOwnedSendsQueuedBehindHeldGate()
    {
        var pipeName = $"dotboxd-terminal-send-{Guid.NewGuid():N}";
        await using var peer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await using var sender = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        var accepting = peer.WaitForConnectionAsync();
        await sender.ConnectAsync().WaitAsync(TestTimeout);
        await accepting.WaitAsync(TestTimeout);
        var connection = new StreamConnection(sender, ownsStream: true);
        Assert.True(connection.SendGate.Wait(0));
        var ownedFrame = CreateOwnedFrame();
        var rawSend = connection.SendValueAsync(CreateRawFrame()).AsTask();
        var ownedSend = connection.SendFrameValueAsync(ownedFrame).AsTask();

        try
        {
            Assert.False(rawSend.IsCompleted);
            Assert.False(ownedSend.IsCompleted);
            await connection.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => rawSend.WaitAsync(TestTimeout));
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => ownedSend.WaitAsync(TestTimeout));
            AssertDisposed(ownedFrame);
        }
        finally
        {
            connection.ReleaseSendGate();
            Assert.Equal(1, connection.SendGate.CurrentCount);
            await connection.DisposeAsync();
        }
    }

    private static byte[] CreateRawFrame()
    {
        var frame = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), 1);
        frame[8] = (byte)MessageType.Request;
        return frame;
    }

    private static PooledBufferWriter CreateOwnedFrame()
    {
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 2, MessageType.Response, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);

    private sealed class NoWriteStream : Stream
    {
        private int _disposeCalls;
        private int _writeCalls;

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);
        public int WriteCalls => Volatile.Read(ref _writeCalls);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writeCalls);
            return default;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Interlocked.Increment(ref _disposeCalls);
            base.Dispose(disposing);
        }
    }
}
