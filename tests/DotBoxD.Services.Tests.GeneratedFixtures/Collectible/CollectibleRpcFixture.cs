using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Peer;
using MessagePack;

namespace DotBoxD.Services.Tests.GeneratedFixtures.Collectible;

[RpcService]
public interface ICollectibleService
{
    Task<CollectibleDto> EchoAsync(CollectibleDto value);
}

[MessagePackObject]
public sealed class CollectibleDto
{
    [Key(0)]
    public int Value { get; set; }
}

[MessagePackObject]
public sealed class CollectibleReplayDto
{
    [SerializationConstructor]
    public CollectibleReplayDto(CollectibleDto payload) => Payload = payload;

    [Key(0)]
    public CollectibleDto Payload { get; }
}

[GeneratedMessagePackResolver]
public partial class CollectibleResolver;

public static class CollectibleRpcFixture
{
    public static IFormatterResolver Resolver => CollectibleResolver.Instance;

    public static async Task RoundTripAsync(RpcPeer client, RpcPeer server, MessagePackRpcSerializer serializer)
    {
        server.Provide<ICollectibleService>(new Service());
        server.Start();
        client.Start();
        var value = await client.Get<ICollectibleService>().EchoAsync(new CollectibleDto { Value = 42 });
        if (value.Value != 42)
            throw new InvalidOperationException("The collectible service did not round-trip its DTO.");

        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, value);
        var decoded = (CollectibleDto)serializer.Deserialize(buffer.WrittenMemory, typeof(CollectibleDto))!;
        if (decoded.Value != 42)
            throw new InvalidOperationException("Runtime-type deserialization did not round-trip its DTO.");

        // A constructor-bound DTO property requires serialized constructor replay validation.
        buffer.Clear();
        serializer.Serialize(buffer, new CollectibleReplayDto(value));
        var replayed = serializer.Deserialize<CollectibleReplayDto>(buffer.WrittenMemory);
        if (replayed.Payload.Value != 42)
            throw new InvalidOperationException("Constructor replay did not round-trip its DTO.");
    }

    private sealed class Service : ICollectibleService
    {
        public Task<CollectibleDto> EchoAsync(CollectibleDto value) => Task.FromResult(value);
    }
}
