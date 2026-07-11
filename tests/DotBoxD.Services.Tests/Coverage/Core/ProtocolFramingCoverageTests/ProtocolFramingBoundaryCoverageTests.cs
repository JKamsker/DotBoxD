using System.Buffers.Binary;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using Xunit;

using static DotBoxD.Services.Tests.Coverage.Core.ProtocolFramingTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class ProtocolFramingBoundaryCoverageTests
{
    [Fact]
    public void GetOutgoingFrameLength_AllowsFrameAtMaximumSize()
    {
        var payloadLength = MessageFramer.MaxMessageSize - MessageFramer.HeaderSize;

        var totalLength = MessageFrameReader.GetOutgoingFrameLength(payloadLength);

        Assert.Equal(MessageFramer.MaxMessageSize, totalLength);
    }

    [Fact]
    public void ValidateOutgoingFrame_AllowsDeclaredLengthEqualToConfiguredMaximum()
    {
        using var frame = MessageFramer.FrameToPayload(7, MessageType.Request, ReadOnlySpan<byte>.Empty);

        MessageFramer.ValidateOutgoingFrame(frame.Span, maxMessageSize: frame.Length);
    }

    [Fact]
    public void TryReadFrame_MaximumSizedRpcFrame_ReturnsEnvelopeAndPayloadSlices()
    {
        var frame = BuildMaximumSizedRpcFrame();

        var ok = MessageFramer.TryReadFrame(
            frame,
            out var id,
            out var type,
            out var envelope,
            out var payload);

        Assert.True(ok);
        Assert.Equal(42, id);
        Assert.Equal(MessageType.Request, type);
        Assert.True(envelope.IsEmpty);
        Assert.Equal(
            MessageFramer.MaxMessageSize - MessageFramer.HeaderSize - MessageFramer.EnvelopeLengthSize,
            payload.Length);
    }

    [Fact]
    public void TryReadFrameHeader_MaximumSizedFrame_ReturnsHeader()
    {
        var frame = BuildMaximumSizedRpcFrame();

        var ok = MessageFramer.TryReadFrameHeader(frame, out var id, out var type);

        Assert.True(ok);
        Assert.Equal(42, id);
        Assert.Equal(MessageType.Request, type);
    }

    [Fact]
    public void FrameToPayload_UndefinedMessageType_ReportsParameterAndStableMessage()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => MessageFramer.FrameToPayload(
                1,
                (MessageType)0x7F,
                ReadOnlySpan<byte>.Empty));

        Assert.Equal("type", ex.ParamName);
        Assert.Contains("Unsupported DotBoxD message type.", ex.Message);
    }

    [Fact]
    public void WriteFramePrefix_UndefinedMessageType_ThrowsBeforeAdvancingWriter()
    {
        using var writer = new PooledBufferWriter();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => MessageFramer.WriteFramePrefix(writer, 1, (MessageType)0x7F));

        Assert.Equal("type", ex.ParamName);
        Assert.Equal(0, writer.WrittenCount);
    }

    [Fact]
    public async Task ReadMessageAsync_MaximumDeclaredLength_ReadsPayloadBeforeRejectingTruncation()
    {
        var header = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), MessageFramer.MaxMessageSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), 55);
        header[8] = (byte)MessageType.Response;
        using var stream = new MemoryStream(header);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(
                    stream,
                    System.Threading.Timeout.InfiniteTimeSpan,
                    CancellationToken.None)
                .AsTaskWithTimeout(FramingTimeout));

        Assert.Contains("payload bytes", ex.Message);
        Assert.DoesNotContain("Invalid DotBoxD frame length", ex.Message);
    }

    [Fact]
    public async Task WriteMessageAsync_FlushesAfterWritingFrame()
    {
        var stream = new FlushTrackingStream();
        var payload = new byte[] { 1, 2, 3 };

        await MessageFramer.WriteMessageAsync(stream, 123, MessageType.StreamItem, payload)
            .AsTaskWithTimeout(FramingTimeout);

        Assert.Equal(1, stream.FlushCount);
        Assert.True(MessageFramer.TryReadFrameHeader(stream.Written, out var id, out var type));
        Assert.Equal(123, id);
        Assert.Equal(MessageType.StreamItem, type);
        Assert.Equal(payload, stream.Written.Slice(MessageFramer.HeaderSize).ToArray());
    }

    private static byte[] BuildMaximumSizedRpcFrame()
    {
        var frame = new byte[MessageFramer.MaxMessageSize];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 42);
        frame[8] = (byte)MessageType.Request;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(MessageFramer.HeaderSize, 4), 0);
        return frame;
    }

    private sealed class FlushTrackingStream : Stream
    {
        private readonly MemoryStream _inner = new();

        public int FlushCount { get; private set; }

        public ReadOnlyMemory<byte> Written => _inner.ToArray();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => FlushCount++;

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FlushCount++;
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inner.Write(buffer.Span);
            return default;
        }
    }
}
