using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Serialization;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackObjectFrameworkScalarRegressionTests
{
    [Theory]
    [MemberData(nameof(FrameworkScalarValues))]
    public void Object_typed_framework_scalar_payloads_round_trip_or_fail_closed(
        object value,
        Type expectedType)
    {
        var serializer = new MessagePackRpcSerializer();

        using var payload = SerializeObjectOrAllowClosedFailure(serializer, value);
        if (payload is null)
        {
            return;
        }

        var roundTrip = serializer.Deserialize<object?>(payload.Memory);

        Assert.NotNull(roundTrip);
        Assert.Equal(expectedType, roundTrip.GetType());
        Assert.Equal(value, roundTrip);
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

    public static TheoryData<object, Type> FrameworkScalarValues()
        => new()
        {
            { Guid.Parse("11111111-2222-3333-4444-555555555555"), typeof(Guid) },
            { new DateTimeOffset(2026, 7, 6, 13, 30, 42, TimeSpan.FromHours(2)), typeof(DateTimeOffset) },
        };

    private static Payload? SerializeObjectOrAllowClosedFailure<T>(
        MessagePackRpcSerializer serializer,
        T value)
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
}
