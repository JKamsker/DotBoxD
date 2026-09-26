using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class MessageFramerIdleTimeoutRegressionTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task ReadMessageAsync_LateHeaderAfterIdleTimeout_DoesNotPublishMessage()
    {
        using var frame = MessageFramer.FrameToPayload(36, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
        using var stream = new LateFrameAfterTimeoutStream(frame.Memory.ToArray());

        MessageFramer.FramedMessage? message = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            message = await MessageFramer.ReadMessageAsync(
                stream,
                IdleTimeout,
                CancellationToken.None);
        });

        try
        {
            var timeout = Assert.IsType<IOException>(exception);
            Assert.Contains("Inbound frame read stalled", timeout.Message, StringComparison.Ordinal);
            Assert.True(stream.TimeoutCancellationObserved);
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

    private sealed class LateFrameAfterTimeoutStream(byte[] frame) : Stream
    {
        private readonly byte[] _frame = frame;
        private int _offset;

        public bool TimeoutCancellationObserved { get; private set; }

        public int ReadCalls { get; private set; }

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
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TimeoutCancellationObserved = true;
            }

            var available = _frame.Length - _offset;
            var count = Math.Min(buffer.Length, available);
            _frame.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
