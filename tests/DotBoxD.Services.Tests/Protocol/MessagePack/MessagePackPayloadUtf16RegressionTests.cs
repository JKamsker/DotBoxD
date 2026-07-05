using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackPayloadUtf16RegressionTests
{
    [Fact]
    public void SerializeToPayload_RejectsMalformedUtf16DirectStringPayload()
    {
        var serializer = new MessagePackRpcSerializer();
        var malformed = "prefix-\uD800-suffix";

        Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(malformed));
    }

    [Fact]
    public void SerializeToPayload_RoundTripsValidSurrogatePairDirectStringPayload()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = "prefix-\uD83D\uDE80-suffix";

        using var payload = serializer.SerializeToPayload(value);
        var roundTrip = serializer.Deserialize<string>(payload.Memory);

        Assert.Equal(value, roundTrip);
    }
}
