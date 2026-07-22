using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.AotSmoke;

public static class AotConstructorReplaySmoke
{
    // Cross the runtime's hot-validator admission threshold. NativeAOT must keep using the
    // reflection path because dynamic code compilation is unavailable there.
    private const int ReplayCount = 8193;

    public static bool Run()
    {
        var serializer = MessagePackRpcSerializer.CreateWithResolver(ReplayResolver.Instance);
        var value = new ReplayDto(42);
        var writer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < ReplayCount; i++)
        {
            serializer.Serialize(writer, value);
            writer.Clear();
        }

        serializer.Serialize(writer, value);
        return serializer.Deserialize<ReplayDto>(writer.WrittenMemory).Id == value.Id;
    }

    public sealed class ReplayDto
    {
        public ReplayDto(int id) => Id = id;

        public int Id { get; }
    }

    private sealed class ReplayResolver : IFormatterResolver
    {
        public static readonly ReplayResolver Instance = new();

        public IMessagePackFormatter<T>? GetFormatter<T>() =>
            typeof(T) == typeof(ReplayDto)
                ? (IMessagePackFormatter<T>)(object)ReplayFormatter.Instance
                : null;
    }

    private sealed class ReplayFormatter : IMessagePackFormatter<ReplayDto>
    {
        public static readonly ReplayFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer,
            ReplayDto value,
            MessagePackSerializerOptions options) =>
            writer.WriteInt32(value.Id);

        public ReplayDto Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options) =>
            new(reader.ReadInt32());
    }
}
