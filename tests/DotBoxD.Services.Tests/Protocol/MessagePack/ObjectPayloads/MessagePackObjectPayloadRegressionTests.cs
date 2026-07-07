using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackObjectPayloadRegressionTests
{
    [Theory]
    [MemberData(nameof(PrimitiveObjectPayloads))]
    public void Primitive_object_payloads_round_trip_exact_shape(object value)
    {
        var serializer = new MessagePackRpcSerializer();

        // The selected primitive values must preserve exact CLR runtime type through
        // MessagePack's primitive object formatter, not just value equality.
        using var payload = serializer.SerializeToPayload<object?>(value);
        var roundTrip = serializer.Deserialize<object?>(payload.Memory);

        Assert.NotNull(roundTrip);
        Assert.Equal(value.GetType(), roundTrip.GetType());
        Assert.Equal(value, roundTrip);
    }

    [Theory]
    [MemberData(nameof(EnumAndAggregateObjectPayloads))]
    public void Object_declared_enum_and_aggregate_payloads_fail_closed(
        ObjectPayloadCase scenario)
    {
        var serializer = new MessagePackRpcSerializer();

        Assert.Throws<MessagePackSerializationException>(() =>
        {
            using var payload = serializer.SerializeToPayload<object?>(scenario.Value);
        });
    }

    public static TheoryData<object> PrimitiveObjectPayloads()
        => new()
        {
            // Avoid MessagePack fixint-overlap values, such as small positive short/int cases,
            // when adding values here. The cases are intentionally chosen to keep wire-format
            // type fidelity stable under MessagePackRpcSerializer's current object behavior.
            "player-one",
            (byte)42,
            (sbyte)-7,
            (short)-1234,
            (ushort)1234,
            123456789,
            123456789U,
            9876543210L,
            9876543210UL,
            12.5F,
            12.5D,
            true,
        };

    public static TheoryData<ObjectPayloadCase> EnumAndAggregateObjectPayloads()
        => new()
        {
            new ObjectPayloadCase("enum", ProbeEnum.Beta),
            new ObjectPayloadCase("flags enum", ProbeFlags.A | ProbeFlags.High),
            new ObjectPayloadCase("tuple", (1, 2)),
            new ObjectPayloadCase("string array", new[] { "left", "right" }),
            new ObjectPayloadCase(
                "dictionary",
                new Dictionary<string, int> { ["left"] = 1, ["right"] = 2 }),
        };

    public sealed record ObjectPayloadCase(
        string Name,
        object Value)
    {
        public override string ToString() => Name;
    }

    private enum ProbeEnum
    {
        Alpha = 1,
        Beta = 2,
    }

    [Flags]
    private enum ProbeFlags : ulong
    {
        A = 1,
        High = 1UL << 63,
    }
}
