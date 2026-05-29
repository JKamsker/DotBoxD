using System.Buffers.Binary;
using ShaRPC.Core.Protocol;
using Xunit;

namespace ShaRPC.Tests;

public class MessageFramerTests
{
    [Fact]
    public void Frame_ShouldCreateValidFrame()
    {
        // Arrange
        var messageId = 42;
        var type = MessageType.Request;
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);
        var span = frame.Span;

        // Assert
        Assert.Equal(MessageFramer.HeaderSize + payload.Length, frame.Length);

        // Check total length (first 4 bytes)
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
        Assert.Equal(frame.Length, totalLength);

        // Check message ID (next 4 bytes)
        var extractedMessageId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        Assert.Equal(messageId, extractedMessageId);

        // Check message type (next 1 byte)
        Assert.Equal((byte)type, span[8]);

        // Check payload
        Assert.Equal(payload, frame.Memory.Slice(MessageFramer.HeaderSize).ToArray());
    }

    [Fact]
    public void Frame_WithEmptyPayload_ShouldCreateValidFrame()
    {
        // Arrange
        var messageId = 1;
        var type = MessageType.Response;
        var payload = Array.Empty<byte>();

        // Act
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);

        // Assert
        Assert.Equal(MessageFramer.HeaderSize, frame.Length);
    }

    [Fact]
    public void TryReadFrame_ShouldRoundTripFrameToPayload()
    {
        // Arrange
        var messageId = 7;
        var type = MessageType.Response;
        var payload = new byte[] { 9, 8, 7, 6 };
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);

        // Act
        var ok = MessageFramer.TryReadFrame(frame.Memory, out var extractedId, out var extractedType, out var extractedPayload);

        // Assert
        Assert.True(ok);
        Assert.Equal(messageId, extractedId);
        Assert.Equal(type, extractedType);
        Assert.Equal(payload, extractedPayload.ToArray());
    }

    [Fact]
    public async Task ReadMessageAsync_ShouldReadValidMessage()
    {
        // Arrange
        var messageId = 123;
        var type = MessageType.Request;
        var payload = new byte[] { 10, 20, 30 };
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);
        using var stream = new MemoryStream(frame.Memory.ToArray());

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        var msg = result.Value;
        try
        {
            Assert.Equal(messageId, msg.MessageId);
            Assert.Equal(type, msg.Type);
            Assert.Equal(payload, msg.Payload.Memory.ToArray());
        }
        finally
        {
            msg.Payload.Dispose();
        }
    }

    [Fact]
    public async Task ReadMessageAsync_WithEmptyStream_ShouldReturnNull()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteMessageAsync_ShouldWriteValidFrame()
    {
        // Arrange
        var messageId = 456;
        var type = MessageType.Error;
        var payload = new byte[] { 100, 200 };
        using var stream = new MemoryStream();

        // Act
        await MessageFramer.WriteMessageAsync(stream, messageId, type, payload);
        stream.Position = 0;
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        var msg = result.Value;
        try
        {
            Assert.Equal(messageId, msg.MessageId);
            Assert.Equal(type, msg.Type);
            Assert.Equal(payload, msg.Payload.Memory.ToArray());
        }
        finally
        {
            msg.Payload.Dispose();
        }
    }

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Error)]
    [InlineData(MessageType.Cancel)]
    public async Task RoundTrip_ShouldPreserveMessageType(MessageType type)
    {
        // Arrange
        var messageId = 789;
        var payload = new byte[] { 1 };
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);
        using var stream = new MemoryStream(frame.Memory.ToArray());

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        var msg = result.Value;
        try
        {
            Assert.Equal(type, msg.Type);
        }
        finally
        {
            msg.Payload.Dispose();
        }
    }

    [Fact]
    public async Task ReadMessageAsync_WithLargePayload_ShouldSucceed()
    {
        // Arrange
        var messageId = 1;
        var type = MessageType.Request;
        var payload = new byte[10000];
        new Random(42).NextBytes(payload);
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);
        using var stream = new MemoryStream(frame.Memory.ToArray());

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        var msg = result.Value;
        try
        {
            Assert.Equal(payload, msg.Payload.Memory.ToArray());
        }
        finally
        {
            msg.Payload.Dispose();
        }
    }
}
