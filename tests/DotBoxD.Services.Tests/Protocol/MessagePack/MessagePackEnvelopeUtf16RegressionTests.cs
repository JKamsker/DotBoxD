using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class MessagePackEnvelopeUtf16RegressionTests
{
    private static readonly string MalformedUtf16 = "prefix-" + new string((char)0xD800, 1) + "-suffix";
    private static readonly string ValidSurrogatePair = "prefix-" + char.ConvertFromUtf32(0x1F600) + "-suffix";

    [Theory]
    [MemberData(nameof(RequestsWithMalformedEnvelopeStrings))]
    public void RpcRequest_rejects_malformed_utf16_envelope_strings(RpcRequest request, string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();

        var ex = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, request));

        Assert.Contains(fieldName, ex.Message);
        Assert.Contains("surrogate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ResponsesWithMalformedEnvelopeStrings))]
    public void RpcResponse_rejects_malformed_utf16_error_strings(RpcResponse response, string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();

        var ex = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, response));

        Assert.Contains(fieldName, ex.Message);
        Assert.Contains("surrogate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RpcRequest_deserialize_surfaces_envelope_validation_message()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteRequestEnvelope(serviceName: string.Empty, methodName: "Call", instanceId: null);

        AssertDeserializeRejects<RpcRequest>(serializer, payload, nameof(RpcRequest.ServiceName), "empty");
    }

    [Fact]
    public void RpcResponse_deserialize_surfaces_envelope_validation_message()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteResponseEnvelope(isSuccess: true, errorMessage: "boom", errorType: null);

        AssertDeserializeRejects<RpcResponse>(serializer, payload, "error fields");
    }

    [Fact]
    public void Valid_surrogate_pairs_roundtrip_in_envelope_strings()
    {
        var serializer = new MessagePackRpcSerializer();

        var request = RoundTrip(
            serializer,
            new RpcRequest
            {
                MessageId = 1,
                ServiceName = "Service" + ValidSurrogatePair,
                MethodName = "Method" + ValidSurrogatePair,
                InstanceId = "Instance" + ValidSurrogatePair,
            });
        var response = RoundTrip(
            serializer,
            new RpcResponse
            {
                MessageId = 1,
                IsSuccess = false,
                ErrorMessage = "Error" + ValidSurrogatePair,
                ErrorType = "Type" + ValidSurrogatePair,
            });

        Assert.EndsWith(ValidSurrogatePair, request.ServiceName, StringComparison.Ordinal);
        Assert.EndsWith(ValidSurrogatePair, request.MethodName, StringComparison.Ordinal);
        Assert.EndsWith(ValidSurrogatePair, request.InstanceId, StringComparison.Ordinal);
        Assert.EndsWith(ValidSurrogatePair, response.ErrorMessage, StringComparison.Ordinal);
        Assert.EndsWith(ValidSurrogatePair, response.ErrorType, StringComparison.Ordinal);
    }

    public static TheoryData<RpcRequest, string> RequestsWithMalformedEnvelopeStrings()
        => new()
        {
            {
                new RpcRequest
                {
                    MessageId = 1,
                    ServiceName = MalformedUtf16,
                    MethodName = "Call",
                },
                nameof(RpcRequest.ServiceName)
            },
            {
                new RpcRequest
                {
                    MessageId = 1,
                    ServiceName = "Service",
                    MethodName = MalformedUtf16,
                },
                nameof(RpcRequest.MethodName)
            },
            {
                new RpcRequest
                {
                    MessageId = 1,
                    ServiceName = "Service",
                    MethodName = "Call",
                    InstanceId = MalformedUtf16,
                },
                nameof(RpcRequest.InstanceId)
            },
        };

    public static TheoryData<RpcResponse, string> ResponsesWithMalformedEnvelopeStrings()
        => new()
        {
            {
                new RpcResponse
                {
                    MessageId = 1,
                    IsSuccess = false,
                    ErrorMessage = MalformedUtf16,
                    ErrorType = "RemoteServiceException",
                },
                nameof(RpcResponse.ErrorMessage)
            },
            {
                new RpcResponse
                {
                    MessageId = 1,
                    IsSuccess = false,
                    ErrorMessage = "boom",
                    ErrorType = MalformedUtf16,
                },
                nameof(RpcResponse.ErrorType)
            },
        };

    private static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        return serializer.Deserialize<T>(writer.WrittenMemory);
    }

    private static void AssertDeserializeRejects<T>(
        MessagePackRpcSerializer serializer,
        byte[] payload,
        params string[] messageFragments)
    {
        var generic = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<T>(payload));
        AssertValidationMessage(generic, messageFragments);

        var nonGeneric = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize(payload, typeof(T)));
        AssertValidationMessage(nonGeneric, messageFragments);
    }

    private static void AssertValidationMessage(
        MessagePackSerializationException exception,
        params string[] messageFragments)
    {
        foreach (var fragment in messageFragments)
        {
            Assert.Contains(fragment, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static byte[] WriteRequestEnvelope(string serviceName, string methodName, string? instanceId)
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(4);
        message.Write("MessageId");
        message.Write(42);
        message.Write("ServiceName");
        message.Write(serviceName);
        message.Write("MethodName");
        message.Write(methodName);
        message.Write("InstanceId");
        WriteNullableString(ref message, instanceId);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    private static byte[] WriteResponseEnvelope(bool isSuccess, string? errorMessage, string? errorType)
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(4);
        message.Write("MessageId");
        message.Write(42);
        message.Write("IsSuccess");
        message.Write(isSuccess);
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
}
