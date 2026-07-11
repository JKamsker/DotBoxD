using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using MessagePack;
using MessagePack.Formatters;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class SerializerFacadeNullGuardTests
{
    [Fact]
    public void CreateWithResolver_NullResolver_ReportsPublicResolverParameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => MessagePackRpcSerializer.CreateWithResolver(null!));

        Assert.Equal("resolver", exception.ParamName);
    }

    [Fact]
    public void Serialize_NullWriter_ReportsPublicWriterParameter()
    {
        var serializer = new MessagePackRpcSerializer();

        var exception = Assert.Throws<ArgumentNullException>(
            () => serializer.Serialize<string>(null!, "value"));

        Assert.Equal("writer", exception.ParamName);
    }

    [Fact]
    public void SerializeToPayload_NullSerializer_ReportsExtensionReceiverParameter()
    {
        ISerializer serializer = null!;

        var exception = Assert.Throws<ArgumentNullException>(
            () => serializer.SerializeToPayload("value"));

        Assert.Equal("serializer", exception.ParamName);
    }

    [Fact]
    public void CreateWithResolver_ValidCustomResolver_StillOverridesDefaultFormatter()
    {
        var resolver = new CustomStringResolver();
        var serializer = MessagePackRpcSerializer.CreateWithResolver(resolver);

        var result = RoundTrip(serializer, "player-1");

        Assert.Equal("custom:player-1", result);
        Assert.Equal(1, resolver.Formatter.DeserializeCalls);
    }

    [Fact]
    public void SerializeToPayload_WithSerializer_RoundTripsPayload()
    {
        var serializer = new MessagePackRpcSerializer();

        using var payload = serializer.SerializeToPayload("value");
        var result = serializer.Deserialize<string>(payload.Memory);

        Assert.Equal("value", result);
    }

    private static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        using var payload = serializer.SerializeToPayload(value);
        return serializer.Deserialize<T>(payload.Memory);
    }

    private sealed class CustomStringResolver : IFormatterResolver
    {
        public CustomStringFormatter Formatter { get; } = new();

        public IMessagePackFormatter<T>? GetFormatter<T>() =>
            typeof(T) == typeof(string)
                ? (IMessagePackFormatter<T>)(object)Formatter
                : null;
    }

    private sealed class CustomStringFormatter : IMessagePackFormatter<string?>
    {
        public int DeserializeCalls { get; private set; }

        public void Serialize(
            ref MessagePackWriter writer,
            string? value,
            MessagePackSerializerOptions options) =>
            writer.Write(value);

        public string? Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            DeserializeCalls++;
            return "custom:" + reader.ReadString();
        }
    }
}
