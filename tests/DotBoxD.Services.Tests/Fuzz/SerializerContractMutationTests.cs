using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace DotBoxD.Services.Tests.Fuzz;

public sealed class SerializerContractMutationTests
{
    [Fact]
    public void Null_configuration_and_arguments_report_the_correct_parameter()
    {
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => new MessagePackRpcSerializer(null!)).ParamName);
        Assert.Equal("resolver", Assert.Throws<ArgumentNullException>(() => MessagePackRpcSerializer.CreateWithResolver(null!)).ParamName);
        Assert.Equal("resolvers", Assert.Throws<ArgumentNullException>(() => MessagePackRpcSerializer.CreateOptions(null!)).ParamName);
        var element = Assert.Throws<ArgumentException>(() => MessagePackRpcSerializer.CreateOptions(StandardResolver.Instance, null!));
        Assert.Equal("resolvers", element.ParamName);
        Assert.Contains("must not contain null", element.Message, StringComparison.Ordinal);
        var serializer = new MessagePackRpcSerializer();
        Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => serializer.Serialize<int>(null!, 1)).ParamName);
        Assert.Equal("type", Assert.Throws<ArgumentNullException>(() => serializer.Deserialize(new byte[] { 0 }, null!)).ParamName);
    }

    [Fact]
    public void Framework_scalars_require_a_declared_type_and_keep_a_useful_diagnostic()
    {
        var serializer = new MessagePackRpcSerializer();
        object[] values = [Guid.Empty, DateTimeOffset.UnixEpoch];
        foreach (var value in values)
        {
            var exception = Assert.Throws<MessagePackSerializationException>(() => serializer.Serialize<object>(new ArrayBufferWriter<byte>(), value));
            Assert.Contains(value.GetType().FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains("without a declared target type", exception.Message, StringComparison.Ordinal);
        }

        Assert.Equal(Guid.Empty, Roundtrip(serializer, Guid.Empty));
        Assert.Equal(DateTimeOffset.UnixEpoch, Roundtrip(serializer, DateTimeOffset.UnixEpoch));
        Assert.Null(Roundtrip<object?>(serializer, null));
        Assert.Equal("value", Roundtrip<object>(serializer, "value"));
    }

    [Fact]
    public void Presets_and_custom_resolvers_preserve_payload_and_envelope_contracts()
    {
        MessagePackRpcSerializer[] serializers =
        [
            new(), MessagePackRpcSerializer.CreateUnityCompatible(),
            MessagePackRpcSerializer.CreateWithResolver(StandardResolver.Instance),
            new(MessagePackRpcSerializer.CreateOptions())
        ];
        foreach (var serializer in serializers)
        {
            Assert.Equal(42, Roundtrip(serializer, 42));
            var bytes = new byte[] { 1, 2, 3, 255 };
            Assert.Equal(bytes, Roundtrip<ReadOnlyMemory<byte>>(serializer, bytes).ToArray());
            Assert.True(serializer.Deserialize<ReadOnlyMemory<byte>>(new byte[] { 0xc0 }).IsEmpty);
            Assert.True(Roundtrip(serializer, ReadOnlyMemory<byte>.Empty).IsEmpty);
            var request = Roundtrip(serializer, new RpcRequest { MessageId = 1, ServiceName = "Service", MethodName = "Call" });
            Assert.Equal("Service", request.ServiceName);
            Assert.Equal("Call", request.MethodName);
        }
    }

    [Theory]
    [InlineData(0xD800, "", "")]
    [InlineData(0xDFFF, "", "")]
    [InlineData(0xD800, "a", "")]
    [InlineData(0xDFFF, "a", "b")]
    public void Malformed_unicode_reports_validation_not_an_indexing_failure(int codeUnit, string prefix, string suffix)
    {
        var text = prefix + (char)codeUnit + suffix;
        var serializer = new MessagePackRpcSerializer();
        var payload = Assert.Throws<MessagePackSerializationException>(() => serializer.Serialize(new ArrayBufferWriter<byte>(), text));
        Assert.Contains("String payload contains malformed UTF-16", payload.ToString(), StringComparison.Ordinal);
        var request = new RpcRequest { MessageId = 1, ServiceName = "Service", MethodName = "Call", InstanceId = text };
        AssertEnvelopeFailure(serializer, request, "RPC request InstanceId contains malformed UTF-16");
        AssertEnvelopeFailure(serializer, new RpcResponse { MessageId = 1, ErrorMessage = text, ErrorType = "Error" },
            "RPC response ErrorMessage contains malformed UTF-16");
        AssertEnvelopeFailure(serializer, new RpcResponse { MessageId = 1, ErrorMessage = "failure", ErrorType = text },
            "RPC response ErrorType contains malformed UTF-16");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Failed_responses_require_both_nonblank_error_details(string? detail)
    {
        var serializer = new MessagePackRpcSerializer();
        var reason = detail is null ? "is missing required" : "contains blank";
        AssertEnvelopeFailure(serializer, new RpcResponse { MessageId = 1, ErrorMessage = detail, ErrorType = "Error" },
            $"Error RPC response {reason} ErrorMessage.");
        AssertEnvelopeFailure(serializer, new RpcResponse { MessageId = 1, ErrorMessage = "failure", ErrorType = detail },
            $"Error RPC response {reason} ErrorType.");
    }

    [Fact]
    public void Failed_response_cannot_include_a_stream()
    {
        AssertEnvelopeFailure(new MessagePackRpcSerializer(), new RpcResponse
        {
            MessageId = 1,
            ErrorMessage = "failure",
            ErrorType = "Error",
            Stream = new RpcStreamHandle(2, RpcStreamKind.Items)
        }, "Error RPC response must not contain a stream handle.");
    }

    private static void AssertEnvelopeFailure<T>(MessagePackRpcSerializer serializer, T value, string message)
    {
        var exception = Assert.Throws<MessagePackSerializationException>(() => serializer.Serialize(new ArrayBufferWriter<byte>(), value));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    private static T Roundtrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        return serializer.Deserialize<T>(writer.WrittenMemory);
    }
}
