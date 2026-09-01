using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class MessageFramerCancellationRegressionTests
{
    [Fact]
    public async Task WriteMessageAsync_PreCanceledToken_DoesNotWriteOrFlush()
    {
        using var stream = new NonCooperativeWriteStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Record.ExceptionAsync(
            () => MessageFramer.WriteMessageAsync(
                stream,
                messageId: 12,
                MessageType.Request,
                new byte[] { 1, 2, 3 },
                cts.Token));

        Assert.True(
            exception is OperationCanceledException,
            $"Expected cancellation before stream I/O; actual exception: {exception?.GetType().Name ?? "<none>"}, " +
            $"writes={stream.WriteCalls}, bytes={stream.BytesWritten}, flushes={stream.FlushCalls}.");
        Assert.Equal(0, stream.WriteCalls);
        Assert.Equal(0, stream.BytesWritten);
        Assert.Equal(0, stream.FlushCalls);
    }

    [Fact]
    public async Task ReadMessageAsync_PreCanceledToken_DoesNotReadFromStream()
    {
        using var frame = MessageFramer.FrameToPayload(34, MessageType.Response, new byte[] { 9 });
        using var stream = new NonCooperativeReadStream(frame.Memory.ToArray());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        MessageFramer.FramedMessage? message = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            message = await MessageFramer.ReadMessageAsync(stream, cts.Token);
        });

        try
        {
            Assert.True(
                exception is OperationCanceledException,
                $"Expected cancellation before stream I/O; actual exception: {exception?.GetType().Name ?? "<none>"}, " +
                $"reads={stream.ReadCalls}, bytes={stream.BytesRead}, completed={message.HasValue}.");
            Assert.Equal(0, stream.ReadCalls);
            Assert.Equal(0, stream.BytesRead);
        }
        finally
        {
            if (message is { } completed)
            {
                completed.Body.Dispose();
            }
        }
    }

    [Fact]
    public async Task ReadMessageAsync_CancellationDuringCompletedHeaderRead_DoesNotPublishMessage()
    {
        using var frame = MessageFramer.FrameToPayload(35, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
        using var cts = new CancellationTokenSource();
        using var stream = new NonCooperativeReadStream(frame.Memory.ToArray(), cts);

        MessageFramer.FramedMessage? message = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            message = await MessageFramer.ReadMessageAsync(stream, cts.Token);
        });

        try
        {
            var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.Equal(cts.Token, cancellation.CancellationToken);
            Assert.Null(message);
            Assert.Equal(1, stream.ReadCalls);
        }
        finally
        {
            if (message is { } completed)
            {
                completed.Body.Dispose();
            }
        }
    }

    [Fact]
    public async Task WriteMessageAsync_CancellationDuringCompletedWrite_DoesNotFlush()
    {
        using var cts = new CancellationTokenSource();
        using var stream = new NonCooperativeWriteStream(cts);

        var exception = await Record.ExceptionAsync(
            () => MessageFramer.WriteMessageAsync(
                stream,
                messageId: 13,
                MessageType.Request,
                new byte[] { 4, 5, 6 },
                cts.Token));

        var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cts.Token, cancellation.CancellationToken);
        Assert.Equal(1, stream.WriteCalls);
        Assert.Equal(0, stream.FlushCalls);
    }

    private sealed class NonCooperativeReadStream : Stream
    {
        private readonly byte[] _buffer;
        private readonly CancellationTokenSource? _cancellationSource;
        private int _offset;

        public NonCooperativeReadStream(byte[] buffer, CancellationTokenSource? cancellationSource = null)
        {
            _buffer = buffer;
            _cancellationSource = cancellationSource;
        }

        public int ReadCalls { get; private set; }

        public int BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            var available = _buffer.Length - _offset;
            if (available <= 0)
            {
                return ValueTask.FromResult(0);
            }

            var count = Math.Min(buffer.Length, available);
            _buffer.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            BytesRead += count;
            _cancellationSource?.Cancel();
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NonCooperativeWriteStream : Stream
    {
        private readonly CancellationTokenSource? _cancellationSource;

        public NonCooperativeWriteStream(CancellationTokenSource? cancellationSource = null) =>
            _cancellationSource = cancellationSource;

        public int WriteCalls { get; private set; }

        public int BytesWritten { get; private set; }

        public int FlushCalls { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => FlushCalls++;

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCalls++;
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCalls++;
            BytesWritten += count;
            _cancellationSource?.Cancel();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            BytesWritten += buffer.Length;
            _cancellationSource?.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
