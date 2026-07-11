using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class RpcRequestWhitespaceEnvelopeValidationTests
{
    [Theory]
    [InlineData("ServiceName")]
    [InlineData("MethodName")]
    public void SerializeRejectsWhitespaceRequiredRequestNames(string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var request = CreateRequest(fieldName);
        var writer = new ArrayBufferWriter<byte>();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, request));

        Assert.Contains(fieldName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ServiceName")]
    [InlineData("MethodName")]
    public void DeserializeRejectsWhitespaceRequiredRequestNames(string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var bytes = WriteRequestMap(fieldName);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(bytes));

        Assert.Contains(fieldName, exception.Message, StringComparison.Ordinal);
    }

    private static RpcRequest CreateRequest(string whitespaceField) =>
        new()
        {
            MessageId = 42,
            ServiceName = whitespaceField == "ServiceName" ? "   " : "Svc",
            MethodName = whitespaceField == "MethodName" ? "\t \t" : "Op",
        };

    private static ReadOnlyMemory<byte> WriteRequestMap(string whitespaceField)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);

        writer.WriteMapHeader(5);
        writer.Write("MessageId");
        writer.Write(42);
        writer.Write("ServiceName");
        writer.Write(whitespaceField == "ServiceName" ? "   " : "Svc");
        writer.Write("MethodName");
        writer.Write(whitespaceField == "MethodName" ? "\t \t" : "Op");
        writer.Write("InstanceId");
        writer.WriteNil();
        writer.Write("Streams");
        writer.WriteNil();
        writer.Flush();

        return buffer.WrittenMemory;
    }
}
