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

        Assert.Contains(fieldName, ex.ToString());
        Assert.Contains("surrogate", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ResponsesWithMalformedEnvelopeStrings))]
    public void RpcResponse_rejects_malformed_utf16_error_strings(RpcResponse response, string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();

        var ex = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, response));

        Assert.Contains(fieldName, ex.ToString());
        Assert.Contains("surrogate", ex.ToString(), StringComparison.OrdinalIgnoreCase);
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
}
