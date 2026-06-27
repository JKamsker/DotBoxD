using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RpcEnvelopeValidationTests
{
    [Fact]
    public void RpcRequest_duplicate_envelope_field_throws()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteRequestWithDuplicateServiceName();

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(payload));
    }

    [Fact]
    public void RpcRequest_missing_message_id_throws()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteRequestWithoutMessageId();

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(payload));
    }

    [Fact]
    public void RpcResponse_duplicate_envelope_field_throws()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteResponseWithDuplicateIsSuccess();

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcResponse>(payload));
    }

    [Fact]
    public void RpcResponse_missing_message_id_throws()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteResponseWithoutMessageId();

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcResponse>(payload));
    }

    private static byte[] WriteRequestWithDuplicateServiceName()
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(4);
        message.Write("MessageId");
        message.Write(42);
        message.Write("ServiceName");
        message.Write("First");
        message.Write("ServiceName");
        message.Write("Second");
        message.Write("MethodName");
        message.Write("Call");
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    private static byte[] WriteRequestWithoutMessageId()
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(2);
        message.Write("ServiceName");
        message.Write("Sample.Service");
        message.Write("MethodName");
        message.Write("Call");
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    private static byte[] WriteResponseWithDuplicateIsSuccess()
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(3);
        message.Write("MessageId");
        message.Write(42);
        message.Write("IsSuccess");
        message.Write(true);
        message.Write("IsSuccess");
        message.Write(false);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    private static byte[] WriteResponseWithoutMessageId()
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(1);
        message.Write("IsSuccess");
        message.Write(true);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }
}
