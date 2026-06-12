using ShaRPC.Core.Serialization;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public class MessagePackRpcSerializerTests
{
    [Fact]
    public void ReadOnlyMemoryByteFields_RoundTripAsBinaryPayload()
    {
        var serializer = new MessagePackRpcSerializer();
        var dto = new BinaryDto { Data = new byte[] { 1, 2, 3, 4 } };

        using var payload = serializer.SerializeToPayload(dto);
        var roundTrip = serializer.Deserialize<BinaryDto>(payload.Memory);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, roundTrip.Data.ToArray());
    }

    [Fact]
    public void RepeatedShortStrings_RoundTripToCachedReference()
    {
        var serializer = new MessagePackRpcSerializer();

        var first = RoundTrip(serializer, "player-1");
        var second = RoundTrip(serializer, "player-1");

        Assert.Equal("player-1", first);
        Assert.Same(first, second);
    }

    [Fact]
    public void LongStrings_AreNotCached()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new string('x', 300);

        var first = RoundTrip(serializer, value);
        var second = RoundTrip(serializer, value);

        Assert.Equal(value, first);
        Assert.Equal(value, second);
        Assert.NotSame(first, second);
    }

    private static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        using var payload = serializer.SerializeToPayload(value);
        return serializer.Deserialize<T>(payload.Memory);
    }

    public sealed class BinaryDto
    {
        public ReadOnlyMemory<byte> Data { get; set; }
    }
}
