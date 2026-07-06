using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using MessagePack;
using Xunit;
using BufferPayload = DotBoxD.Services.Buffers.Payload;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackComputedDtoRegressionTests
{
    [Fact]
    public void Constructor_only_getter_dto_round_trips()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ConstructorOnlyDto(5);

        using var payload = serializer.SerializeToPayload(value);
        var roundTrip = serializer.Deserialize<ConstructorOnlyDto>(payload.Memory);

        Assert.Equal(value.Id, roundTrip.Id);
    }

    [Fact]
    public void Computed_get_only_dto_round_trip_preserves_serialized_value_or_fails_closed()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ComputedGetOnlyDto(5);

        Assert.Equal(6, value.Id);

        using var payload = TrySerializeOrNull(serializer, value);
        if (payload is null)
        {
            return;
        }

        var roundTrip = serializer.Deserialize<ComputedGetOnlyDto>(payload.Memory);

        Assert.Equal(value.Id, roundTrip.Id);
    }

    [Fact]
    public void Computed_get_only_dto_hidden_as_object_fails_closed()
    {
        var serializer = new MessagePackRpcSerializer();
        object value = new ComputedGetOnlyDto(5);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(value));

        Assert.Contains(
            "cannot be serialized without changing constructor-bound get-only values",
            exception.Message);
    }

    [Fact]
    public void Computed_get_only_dto_hidden_as_interface_fails_closed()
    {
        var serializer = new MessagePackRpcSerializer();
        IComputedDto value = new ComputedGetOnlyDto(5);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(value));

        Assert.Contains(
            "cannot be serialized without changing constructor-bound get-only values",
            exception.Message);
    }

    private static BufferPayload? TrySerializeOrNull(
        MessagePackRpcSerializer serializer,
        ComputedGetOnlyDto value)
    {
        try
        {
            return serializer.SerializeToPayload(value);
        }
        catch (MessagePackSerializationException)
        {
            return null;
        }
    }

    public sealed class ConstructorOnlyDto
    {
        public ConstructorOnlyDto(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    private interface IComputedDto
    {
        int Id { get; }
    }

    public sealed class ComputedGetOnlyDto : IComputedDto
    {
        public ComputedGetOnlyDto(int id)
        {
            Id = id + 1;
        }

        public int Id { get; }
    }
}
