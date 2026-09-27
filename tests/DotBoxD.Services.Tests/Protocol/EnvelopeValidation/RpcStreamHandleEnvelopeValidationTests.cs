using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class RpcStreamHandleEnvelopeValidationTests
{
    [Theory]
    [InlineData("StreamId", false)]
    [InlineData("Kind", false)]
    [InlineData("StreamId", true)]
    [InlineData("Kind", true)]
    public void MissingRequiredField_IsRejected(string missingField, bool runtimeType)
    {
        var bytes = WriteHandle(missingField == "StreamId"
            ? [("Kind", 1)]
            : [("StreamId", 7)]);

        var error = Assert.Throws<MessagePackSerializationException>(() => Deserialize(bytes, runtimeType));

        Assert.Contains($"missing required {missingField}", error.Message);
    }

    [Theory]
    [InlineData("StreamId", false)]
    [InlineData("Kind", false)]
    [InlineData("StreamId", true)]
    [InlineData("Kind", true)]
    public void DuplicateKnownField_IsRejected(string duplicateField, bool runtimeType)
    {
        var bytes = WriteHandle(("StreamId", 7), ("Kind", 1),
            (duplicateField, duplicateField == "StreamId" ? 7 : 1));

        var error = Assert.Throws<MessagePackSerializationException>(() => Deserialize(bytes, runtimeType));

        Assert.Contains($"duplicate {duplicateField}", error.Message);
    }

    [Theory]
    [InlineData(RpcStreamKind.Binary, false)]
    [InlineData(RpcStreamKind.Items, false)]
    [InlineData(RpcStreamKind.Binary, true)]
    [InlineData(RpcStreamKind.Items, true)]
    public void ValidHandle_RoundTrips(RpcStreamKind kind, bool runtimeType)
    {
        var serializer = new MessagePackRpcSerializer();
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, new RpcStreamHandle(7, kind));

        var result = Deserialize(buffer.WrittenMemory, runtimeType);

        Assert.Equal(7, result.StreamId);
        Assert.Equal(kind, result.Kind);
    }

    [Fact]
    public void UnknownNestedField_IsSkippedAndFollowingFieldsAreRead()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(3);
        writer.Write("Extension");
        writer.WriteArrayHeader(1);
        writer.WriteMapHeader(1);
        writer.Write("value");
        writer.Write(42);
        writer.Write("Kind");
        writer.Write(1);
        writer.Write("StreamId");
        writer.Write(7);
        writer.Flush();

        var result = Deserialize(buffer.WrittenMemory, runtimeType: false);

        Assert.Equal(7, result.StreamId);
        Assert.Equal(RpcStreamKind.Binary, result.Kind);
    }

    private static RpcStreamHandle Deserialize(ReadOnlyMemory<byte> bytes, bool runtimeType)
    {
        var serializer = new MessagePackRpcSerializer();
        return runtimeType
            ? Assert.IsType<RpcStreamHandle>(serializer.Deserialize(bytes, typeof(RpcStreamHandle)))
            : serializer.Deserialize<RpcStreamHandle>(bytes);
    }

    private static ReadOnlyMemory<byte> WriteHandle(params (string Name, int Value)[] fields)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(fields.Length);
        foreach (var (name, value) in fields)
        {
            writer.Write(name);
            writer.Write(value);
        }

        writer.Flush();
        return buffer.WrittenMemory;
    }
}
