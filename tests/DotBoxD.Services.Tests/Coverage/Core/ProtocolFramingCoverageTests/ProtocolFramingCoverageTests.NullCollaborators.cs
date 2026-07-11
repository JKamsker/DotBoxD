using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.ProtocolFramingTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class MessageFramerCoverageTests
{
    [Fact]
    public void WriteFrame_NullWriter_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => MessageFramer.WriteFrame(null!, 1, MessageType.Request, ReadOnlySpan<byte>.Empty));

        Assert.Equal("writer", ex.ParamName);
    }

    [Fact]
    public void FrameMessage_NullSerializer_ThrowsArgumentNullException()
    {
        var request = new RpcRequest { MessageId = 1, ServiceName = "Calc", MethodName = "Add" };

        var ex = Assert.Throws<ArgumentNullException>(
            () => MessageFramer.FrameMessage(
                null!,
                1,
                MessageType.Request,
                request,
                ReadOnlySpan<byte>.Empty));

        Assert.Equal("serializer", ex.ParamName);
    }

    [Fact]
    public void FrameRequest_NullSerializer_ThrowsArgumentNullException()
    {
        var request = new RpcRequest { MessageId = 1, ServiceName = "Calc", MethodName = "Add" };

        var ex = Assert.Throws<ArgumentNullException>(
            () => MessageFramer.FrameRequest<object, object>(
                null!,
                1,
                MessageType.Request,
                request,
                new { Left = 1, Right = 2 }));

        Assert.Equal("serializer", ex.ParamName);
    }

    [Fact]
    public async Task ReadMessageAsync_NullStream_ThrowsArgumentNullException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => MessageFramer.ReadMessageAsync(null!).AsTaskWithTimeout(FramingTimeout));

        Assert.Equal("stream", ex.ParamName);
    }

    [Fact]
    public async Task WriteMessageAsync_NullStream_ThrowsArgumentNullException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => MessageFramer.WriteMessageAsync(
                null!,
                1,
                MessageType.Request,
                ReadOnlyMemory<byte>.Empty).AsTaskWithTimeout(FramingTimeout));

        Assert.Equal("stream", ex.ParamName);
    }

    [Fact]
    public void FrameMessage_WithValidSerializer_RoundTripsEnvelopeAndPayload()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest { MessageId = 42, ServiceName = "Calc", MethodName = "Add" };
        var payload = new byte[] { 1, 2, 3 };

        using var frame = MessageFramer.FrameMessage(serializer, 42, MessageType.Request, request, payload);
        var ok = MessageFramer.TryReadFrame(
            frame.Memory,
            out var messageId,
            out var type,
            out var envelope,
            out var framedPayload);

        Assert.True(ok);
        Assert.Equal(42, messageId);
        Assert.Equal(MessageType.Request, type);
        Assert.Equal(payload, framedPayload.ToArray());

        var roundTripped = serializer.Deserialize<RpcRequest>(envelope);
        Assert.Equal(request.MessageId, roundTripped.MessageId);
        Assert.Equal(request.ServiceName, roundTripped.ServiceName);
        Assert.Equal(request.MethodName, roundTripped.MethodName);
    }
}
