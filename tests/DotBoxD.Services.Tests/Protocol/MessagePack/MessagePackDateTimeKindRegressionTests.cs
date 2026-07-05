using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public class MessagePackDateTimeKindRegressionTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void DateTime_round_trip_preserves_ticks_and_kind(DateTimeKind kind)
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new DateTime(638874012345678901, kind);

        var roundTrip = RoundTrip(serializer, value);

        Assert.Equal(value.Ticks, roundTrip.Ticks);
        Assert.Equal(value.Kind, roundTrip.Kind);
    }

    private static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        return serializer.Deserialize<T>(writer.WrittenMemory);
    }
}
