using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackObjectFrameworkScalarRegressionTests
{
    [Theory]
    [MemberData(nameof(FrameworkScalarValues))]
    public void Object_typed_framework_scalar_payloads_round_trip_or_fail_closed(
        FrameworkScalarCase scenario)
    {
        var serializer = new MessagePackRpcSerializer();

        if (scenario.ExpectsSerializationFailure)
        {
            Assert.Throws<MessagePackSerializationException>(() =>
            {
                using var payload = serializer.SerializeToPayload<object?>(scenario.Value);
            });

            return;
        }

        using var payload = serializer.SerializeToPayload<object?>(scenario.Value);
        var roundTrip = serializer.Deserialize<object?>(payload.Memory);

        Assert.NotNull(roundTrip);
        Assert.Equal(scenario.ExpectedType, roundTrip.GetType());
        Assert.Equal(scenario.Value, roundTrip);
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData(42L)]
    [InlineData(true)]
    public void Primitive_object_payload_control_still_round_trips<T>(T value)
    {
        var serializer = new MessagePackRpcSerializer();

        using var payload = serializer.SerializeToPayload<object?>(value);
        var roundTrip = serializer.Deserialize<object?>(payload.Memory);
        var typed = Assert.IsType<T>(roundTrip);

        Assert.Equal(value, typed);
    }

    public static TheoryData<FrameworkScalarCase> FrameworkScalarValues()
        => new()
        {
            FrameworkScalarCase.FailsSerialization(
                "guid",
                Guid.Parse("11111111-2222-3333-4444-555555555555")),
            FrameworkScalarCase.FailsSerialization(
                "date time offset",
                new DateTimeOffset(2026, 7, 6, 13, 30, 42, TimeSpan.FromHours(2))),
        };

    public sealed record FrameworkScalarCase(
        string Name,
        object Value,
        Type? ExpectedType,
        bool ExpectsSerializationFailure)
    {
        public static FrameworkScalarCase PreservesShape(string name, object value, Type expectedType) =>
            new(name, value, expectedType, ExpectsSerializationFailure: false);

        public static FrameworkScalarCase FailsSerialization(string name, object value) =>
            new(name, value, ExpectedType: null, ExpectsSerializationFailure: true);

        public override string ToString() => Name;
    }
}
