using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
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

        using var payload = serializer.SerializeToPayload<object?>(value);
        var roundTrip = serializer.Deserialize<object?>(payload.Memory);

        Assert.NotNull(roundTrip);
        Assert.Equal(value.GetType(), roundTrip.GetType());
        Assert.Equal(value, roundTrip);
    }

    [Theory]
    [MemberData(nameof(EnumAndAggregateObjectPayloads))]
    public void Object_declared_enum_and_aggregate_payloads_preserve_shape_or_fail_closed(
        ObjectPayloadCase scenario)
    {
        var serializer = new MessagePackRpcSerializer();

        var payload = TrySerializeObject(serializer, scenario.Value);
        if (payload is null)
        {
            return;
        }

        using (payload)
        {
            var roundTrip = serializer.Deserialize<object?>(payload.Memory);

            Assert.NotNull(roundTrip);
            Assert.Equal(scenario.ExpectedType, roundTrip.GetType());
            AssertEquivalent(scenario.Value, roundTrip);
        }
    }

    public static TheoryData<object> PrimitiveObjectPayloads()
        => new()
        {
            "player-one",
            9876543210L,
            true,
        };

    public static TheoryData<ObjectPayloadCase> EnumAndAggregateObjectPayloads()
        => new()
        {
            new ObjectPayloadCase("enum", ProbeEnum.Beta, typeof(ProbeEnum)),
            new ObjectPayloadCase("flags enum", ProbeFlags.A | ProbeFlags.High, typeof(ProbeFlags)),
            new ObjectPayloadCase("tuple", (1, 2), typeof((int, int))),
            new ObjectPayloadCase("string array", new[] { "left", "right" }, typeof(string[])),
            new ObjectPayloadCase(
                "dictionary",
                new Dictionary<string, int> { ["left"] = 1, ["right"] = 2 },
                typeof(Dictionary<string, int>)),
        };

    private static Payload? TrySerializeObject(MessagePackRpcSerializer serializer, object value)
    {
        try
        {
            return serializer.SerializeToPayload<object?>(value);
        }
        catch (MessagePackSerializationException)
        {
            return null;
        }
    }

    private static void AssertEquivalent(object expected, object actual)
    {
        switch (expected)
        {
            case string[] expectedArray:
                var actualArray = Assert.IsType<string[]>(actual);
                Assert.Equal(expectedArray, actualArray);
                break;
            case Dictionary<string, int> expectedDictionary:
                var actualDictionary = Assert.IsType<Dictionary<string, int>>(actual);
                Assert.Equal(expectedDictionary.Count, actualDictionary.Count);
                foreach (var item in expectedDictionary)
                {
                    Assert.True(actualDictionary.TryGetValue(item.Key, out var value));
                    Assert.Equal(item.Value, value);
                }

                break;
            default:
                Assert.Equal(expected, actual);
                break;
        }
    }

    public sealed record ObjectPayloadCase(string Name, object Value, Type ExpectedType)
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
