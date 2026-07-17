using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class RpcEnvelopeReadStateTests
{
    [Fact]
    public void RpcRequest_ReadsReverseOrderedFieldsAroundUnknownAndStreamValues()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteReverseOrderedRequest(serializer);

        var request = serializer.Deserialize<RpcRequest>(payload);

        Assert.Equal(88, request.MessageId);
        Assert.Equal("ReverseService", request.ServiceName);
        Assert.Equal("ReverseCall", request.MethodName);
        Assert.Equal("instance-88", request.InstanceId);
        var stream = Assert.Single(request.Streams!);
        Assert.Equal(88, stream.StreamId);
        Assert.Equal(RpcStreamKind.Items, stream.Kind);
    }

    [Fact]
    public void RpcResponse_ReadsReverseOrderedFieldsAroundUnknownAndStreamValues()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteReverseOrderedResponse(serializer);

        var response = serializer.Deserialize<RpcResponse>(payload);

        Assert.Equal(91, response.MessageId);
        Assert.True(response.IsSuccess);
        Assert.Null(response.ErrorMessage);
        Assert.Null(response.ErrorType);
        Assert.Equal(91, response.Stream?.StreamId);
        Assert.Equal(RpcStreamKind.Binary, response.Stream?.Kind);
    }

    [Fact]
    public void GenericRequestDeserialize_RejectsTrailingBytes()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = SerializeWithTrailingNil(
            serializer,
            new RpcRequest
            {
                MessageId = 42,
                ServiceName = "Service",
                MethodName = "Call",
            });

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(payload));

        Assert.Contains("Trailing bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeTypedResponseDeserialize_RejectsTrailingBytes()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = SerializeWithTrailingNil(
            serializer,
            new RpcResponse { MessageId = 42, IsSuccess = true });

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize(payload, typeof(RpcResponse)));

        Assert.Contains("Trailing bytes", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] WriteReverseOrderedRequest(MessagePackRpcSerializer serializer)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(6);
        writer.Write("Streams");
        writer.WriteArrayHeader(1);
        MessagePackSerializer.Serialize(
            ref writer,
            new RpcStreamHandle(88, RpcStreamKind.Items),
            serializer.Options);
        writer.Write("Future");
        writer.WriteMapHeader(1);
        writer.Write("Nested");
        writer.WriteArrayHeader(2);
        writer.Write(1);
        writer.Write(2);
        writer.Write("MethodName");
        writer.Write("ReverseCall");
        writer.Write("ServiceName");
        writer.Write("ReverseService");
        writer.Write("InstanceId");
        writer.Write("instance-88");
        writer.Write("MessageId");
        writer.Write(88);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteReverseOrderedResponse(MessagePackRpcSerializer serializer)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(6);
        writer.Write("Stream");
        MessagePackSerializer.Serialize(
            ref writer,
            new RpcStreamHandle(91, RpcStreamKind.Binary),
            serializer.Options);
        writer.Write("Future");
        writer.WriteArrayHeader(2);
        writer.Write("one");
        writer.Write("two");
        writer.Write("ErrorType");
        writer.WriteNil();
        writer.Write("ErrorMessage");
        writer.WriteNil();
        writer.Write("IsSuccess");
        writer.Write(true);
        writer.Write("MessageId");
        writer.Write(91);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] SerializeWithTrailingNil<T>(MessagePackRpcSerializer serializer, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, value);

        var writer = new MessagePackWriter(buffer);
        writer.WriteNil();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }
}
