using System.Buffers;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class MessageFramerPayloadCancellationRegressionTests
{
    [Fact]
    public async Task WriteMessageAsync_PayloadAccessCancelsToken_DoesNotWriteOrFlush()
    {
        using var cancellation = new CancellationTokenSource();
        using var payload = new CancellationTriggeringMemoryManager([1, 2, 3], cancellation);
        using var stream = new NonCooperativeWriteStream();

        var exception = await Record.ExceptionAsync(
            () => MessageFramer.WriteMessageAsync(
                stream,
                messageId: 37,
                MessageType.Request,
                payload.Memory,
                cancellation.Token));

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(2, payload.SpanCalls);
        Assert.Equal(0, stream.WriteCalls);
        Assert.Equal(0, stream.BytesWritten);
        Assert.Equal(0, stream.FlushCalls);
    }

    [Fact]
    public async Task WriteMessageAsync_NormalPayload_WritesAndFlushesFrame()
    {
        using var stream = new NonCooperativeWriteStream();

        await MessageFramer.WriteMessageAsync(
            stream,
            messageId: 38,
            MessageType.Response,
            new byte[] { 4, 5, 6 },
            CancellationToken.None);

        Assert.Equal(1, stream.WriteCalls);
        Assert.Equal(MessageFramer.HeaderSize + 3, stream.BytesWritten);
        Assert.Equal(1, stream.FlushCalls);
    }

    private sealed class CancellationTriggeringMemoryManager(
        byte[] buffer,
        CancellationTokenSource cancellation) : MemoryManager<byte>
    {
        private readonly byte[] _buffer = buffer;
        private readonly CancellationTokenSource _cancellation = cancellation;

        public int SpanCalls { get; private set; }

        public override Span<byte> GetSpan()
        {
            SpanCalls++;
            if (SpanCalls == 2)
            {
                _cancellation.Cancel();
            }

            return _buffer;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin() { }

        protected override void Dispose(bool disposing) { }
    }

    private sealed class NonCooperativeWriteStream : Stream
    {
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

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
    }
}
