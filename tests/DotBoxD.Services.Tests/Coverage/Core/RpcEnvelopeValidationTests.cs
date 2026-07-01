using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RpcEnvelopeValidationTests
{
    private const int DeepUnknownFieldDepth = 1000;

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
    public void RpcRequest_unknown_deeply_nested_envelope_field_throws()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteRequestWithDeepUnknownField();

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

    [Fact]
    public void RpcResponse_missing_success_flag_throws()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteResponseWithoutIsSuccess();

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcResponse>(payload));
    }

    [Theory]
    [InlineData("boom", "RemoteServiceException")]
    [InlineData("boom", null)]
    [InlineData(null, "RemoteServiceException")]
    public void RpcResponse_success_with_error_fields_throws(string? errorMessage, string? errorType)
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteSuccessfulResponseWithErrorFields(errorMessage, errorType);

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcResponse>(payload));
    }

    [Fact]
    public void RpcResponse_error_with_stream_throws()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteErrorResponseWithStream(serializer);

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcResponse>(payload));
    }

    [Fact]
    public void RpcResponse_unknown_deeply_nested_envelope_field_throws()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteResponseWithDeepUnknownField();

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

    private static byte[] WriteRequestWithDeepUnknownField()
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(4);
        message.Write("MessageId");
        message.Write(42);
        message.Write("ServiceName");
        message.Write("Sample.Service");
        message.Write("MethodName");
        message.Write("Call");
        message.Write("Future");
        WriteDeeplyNestedArrays(ref message);
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

    private static byte[] WriteResponseWithDeepUnknownField()
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(3);
        message.Write("MessageId");
        message.Write(42);
        message.Write("IsSuccess");
        message.Write(true);
        message.Write("Future");
        WriteDeeplyNestedArrays(ref message);
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

    private static byte[] WriteResponseWithoutIsSuccess()
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(1);
        message.Write("MessageId");
        message.Write(42);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    private static byte[] WriteSuccessfulResponseWithErrorFields(string? errorMessage, string? errorType)
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(4);
        message.Write("MessageId");
        message.Write(7);
        message.Write("IsSuccess");
        message.Write(true);
        message.Write("ErrorMessage");
        WriteNullableString(ref message, errorMessage);
        message.Write("ErrorType");
        WriteNullableString(ref message, errorType);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    private static void WriteNullableString(ref MessagePackWriter message, string? value)
    {
        if (value is null)
        {
            message.WriteNil();
            return;
        }

        message.Write(value);
    }

    private static byte[] WriteErrorResponseWithStream(MessagePackRpcSerializer serializer)
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(5);
        message.Write("MessageId");
        message.Write(42);
        message.Write("IsSuccess");
        message.Write(false);
        message.Write("ErrorMessage");
        message.Write("boom");
        message.Write("ErrorType");
        message.Write("RemoteServiceException");
        message.Write("Stream");
        MessagePackSerializer.Serialize(
            ref message,
            new RpcStreamHandle(701, RpcStreamKind.Binary),
            serializer.Options);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    private static void WriteDeeplyNestedArrays(ref MessagePackWriter message)
    {
        for (var i = 0; i < DeepUnknownFieldDepth; i++)
        {
            message.WriteArrayHeader(1);
        }

        message.WriteNil();
    }
}
