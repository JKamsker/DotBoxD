using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class FailedResponseErrorDetailsTests
{
    [Theory]
    [InlineData(null, "RemoteServiceException")]
    [InlineData("boom", null)]
    [InlineData(null, null)]
    public void RpcResponse_failed_encode_requires_error_details(
        string? errorMessage,
        string? errorType)
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        var response = new RpcResponse
        {
            MessageId = 7,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType,
        };

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, response));
    }

    [Theory]
    [InlineData(false, null, true, "RemoteServiceException")]
    [InlineData(true, "boom", false, null)]
    [InlineData(true, null, true, "RemoteServiceException")]
    [InlineData(true, "boom", true, null)]
    [InlineData(true, null, true, null)]
    public void RpcResponse_failed_decode_requires_error_details(
        bool includeErrorMessage,
        string? errorMessage,
        bool includeErrorType,
        string? errorType)
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteFailedResponse(
            includeErrorMessage,
            errorMessage,
            includeErrorType,
            errorType);

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcResponse>(payload));
    }

    private static byte[] WriteFailedResponse(
        bool includeErrorMessage,
        string? errorMessage,
        bool includeErrorType,
        string? errorType)
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        var fieldCount = 2 + (includeErrorMessage ? 1 : 0) + (includeErrorType ? 1 : 0);
        message.WriteMapHeader(fieldCount);
        message.Write("MessageId");
        message.Write(7);
        message.Write("IsSuccess");
        message.Write(false);

        if (includeErrorMessage)
        {
            message.Write("ErrorMessage");
            WriteNullableString(ref message, errorMessage);
        }

        if (includeErrorType)
        {
            message.Write("ErrorType");
            WriteNullableString(ref message, errorType);
        }

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
