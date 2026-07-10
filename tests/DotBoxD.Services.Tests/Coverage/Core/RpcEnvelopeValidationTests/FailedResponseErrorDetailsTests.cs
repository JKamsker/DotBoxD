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
    [InlineData("", "RemoteServiceException", "ErrorMessage")]
    [InlineData("   ", "RemoteServiceException", "ErrorMessage")]
    [InlineData("boom", "", "ErrorType")]
    [InlineData("boom", "   ", "ErrorType")]
    public void RpcResponse_failed_encode_rejects_blank_error_details(
        string errorMessage,
        string errorType,
        string fieldName)
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

        AssertInvalidErrorDetails(
            () => serializer.Serialize(writer, response),
            fieldName);
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

    [Theory]
    [InlineData("", "RemoteServiceException", "ErrorMessage")]
    [InlineData("   ", "RemoteServiceException", "ErrorMessage")]
    [InlineData("boom", "", "ErrorType")]
    [InlineData("boom", "   ", "ErrorType")]
    public void RpcResponse_failed_decode_rejects_blank_error_details(
        string errorMessage,
        string errorType,
        string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteFailedResponse(
            includeErrorMessage: true,
            errorMessage,
            includeErrorType: true,
            errorType);

        AssertInvalidErrorDetails(
            () => serializer.Deserialize<RpcResponse>(payload),
            fieldName);
    }

    [Fact]
    public void RpcResponse_failed_round_trips_nonblank_error_details()
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        var response = new RpcResponse
        {
            MessageId = 7,
            IsSuccess = false,
            ErrorMessage = "boom",
            ErrorType = "RemoteServiceException",
        };

        serializer.Serialize(writer, response);

        var decoded = serializer.Deserialize<RpcResponse>(writer.WrittenMemory.ToArray());
        Assert.False(decoded.IsSuccess);
        Assert.Equal(response.MessageId, decoded.MessageId);
        Assert.Equal(response.ErrorMessage, decoded.ErrorMessage);
        Assert.Equal(response.ErrorType, decoded.ErrorType);
    }

    private static void AssertInvalidErrorDetails(Action action, string fieldName)
    {
        var ex = Assert.Throws<MessagePackSerializationException>(action);
        Assert.Contains(fieldName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("error", ex.Message, StringComparison.OrdinalIgnoreCase);
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
