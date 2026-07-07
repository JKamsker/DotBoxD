using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using Xunit;

using static DotBoxD.Services.Tests.Coverage.Core.ProtocolFramingTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class ProtocolFramingFrameRequestCoverageTests
{
    [Fact]
    public void RentFrameWriter_InitialCapacityIncludesEnvelopePrefixAndKnownPayload()
    {
        const int knownPayloadLength = 500;

        using var writer = MessageFramer.RentFrameWriter(knownPayloadLength);

        Assert.True(
            writer.GetMemory().Length >=
            MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize + knownPayloadLength);
    }

    [Fact]
    public void RentFrameMessage_CopiesNonEmptyPayloadIntoReturnedWriter()
    {
        var serializer = NewSerializer();
        var envelope = new RpcRequest
        {
            MessageId = 88,
            ServiceName = "Svc",
            MethodName = "Call",
        };
        var payload = new byte[] { 9, 8, 7 };

        using var writer = MessageFramer.RentFrameMessage(
            serializer,
            88,
            MessageType.Request,
            envelope,
            payload);

        Assert.True(MessageFramer.TryReadFrame(
            writer.WrittenMemory,
            out var id,
            out var type,
            out var serializedEnvelope,
            out var serializedPayload));
        Assert.Equal(88, id);
        Assert.Equal(MessageType.Request, type);
        Assert.Equal(payload, serializedPayload.ToArray());
        Assert.Equal("Svc", serializer.Deserialize<RpcRequest>(serializedEnvelope).ServiceName);
    }

    [Fact]
    public void RentFrameMessage_RethrowsSerializerFailure()
    {
        var failure = new InvalidOperationException("serializer failed");
        var serializer = new ThrowingSerializer(failure);

        var ex = Assert.Throws<InvalidOperationException>(
            () => MessageFramer.RentFrameMessage(
                serializer,
                1,
                MessageType.Request,
                new RpcRequest { MessageId = 1, ServiceName = "Svc", MethodName = "Call" },
                ReadOnlySpan<byte>.Empty));

        Assert.Same(failure, ex);
    }

    [Fact]
    public void FrameRequest_SerializesEnvelopeAndArgumentIntoSeparateSlices()
    {
        var serializer = new MessagePackRpcSerializer();
        var envelope = new RpcRequest
        {
            MessageId = 71,
            ServiceName = "Svc",
            MethodName = "Sum",
        };

        using var frame = MessageFramer.FrameRequest(
            serializer,
            71,
            MessageType.Request,
            envelope,
            8675309);

        Assert.True(MessageFramer.TryReadFrame(
            frame.Memory,
            out var id,
            out var type,
            out var serializedEnvelope,
            out var serializedArgument));
        Assert.Equal(71, id);
        Assert.Equal(MessageType.Request, type);

        var roundTripped = serializer.Deserialize<RpcRequest>(serializedEnvelope);
        Assert.Equal(envelope.ServiceName, roundTripped.ServiceName);
        Assert.Equal(envelope.MethodName, roundTripped.MethodName);
        Assert.Equal(8675309, serializer.Deserialize<int>(serializedArgument));
    }

    [Fact]
    public async Task ReadMessageAsync_ExactFrame_DoesNotReadPastFilledBuffers()
    {
        var body = new byte[] { 4, 5 };
        using var frame = MessageFramer.FrameToPayload(99, MessageType.StreamItem, body);
        using var stream = new ExactFrameReadStream(frame.Memory.ToArray());

        var result = await MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(FramingTimeout);

        Assert.NotNull(result);
        var message = result!.Value;
        try
        {
            Assert.Equal(99, message.MessageId);
            Assert.Equal(MessageType.StreamItem, message.Type);
            Assert.Equal(body, message.Body.Memory.ToArray());
        }
        finally
        {
            message.Body.Dispose();
        }

        Assert.Equal(2, stream.ReadCount);
    }

    private sealed class ThrowingSerializer(Exception failure) : ISerializer
    {
        public void Serialize<T>(IBufferWriter<byte> writer, T value) => throw failure;

        public T Deserialize<T>(ReadOnlyMemory<byte> data) =>
            throw new NotSupportedException();

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            throw new NotSupportedException();
    }

    private sealed class ExactFrameReadStream(byte[] frame) : Stream
    {
        private int _offset;

        public int ReadCount { get; private set; }

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
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length == 0)
            {
                throw new InvalidOperationException("The framer read past an already-filled buffer.");
            }

            if (_offset == frame.Length)
            {
                return ValueTask.FromResult(0);
            }

            ReadCount++;
            var bytesToCopy = Math.Min(buffer.Length, frame.Length - _offset);
            frame.AsSpan(_offset, bytesToCopy).CopyTo(buffer.Span);
            _offset += bytesToCopy;
            return ValueTask.FromResult(bytesToCopy);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
