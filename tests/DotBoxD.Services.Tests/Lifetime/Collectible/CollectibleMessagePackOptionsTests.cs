using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Tests.GeneratedFixtures.Collectible;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Lifetime.Collectible;

public sealed class CollectibleMessagePackOptionsTests
{
    [Fact]
    public void Missing_dto_formatters_fail_without_dynamic_fallback()
    {
        var serializer = new MessagePackRpcSerializer(MessagePackRpcSerializer.CreateCollectibleOptions());
        Assert.Throws<MessagePackSerializationException>(() =>
            serializer.Serialize(new ArrayBufferWriter<byte>(), new CollectibleDto { Value = 42 }));
    }

    [Fact]
    public void Fresh_options_support_builtins_and_validate_resolvers()
    {
        var options = MessagePackRpcSerializer.CreateCollectibleOptions();
        Assert.NotSame(options.Resolver, MessagePackRpcSerializer.CreateCollectibleOptions().Resolver);
        var serializer = new MessagePackRpcSerializer(options);
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, 42);
        Assert.Equal(42, serializer.Deserialize<int>(buffer.WrittenMemory));
        Assert.Throws<ArgumentNullException>(() => MessagePackRpcSerializer.CreateCollectibleOptions(null!));
        Assert.Throws<ArgumentException>(() => MessagePackRpcSerializer.CreateCollectibleOptions(new IFormatterResolver[] { null! }));
    }
}
