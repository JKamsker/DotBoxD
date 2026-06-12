using MessagePack;
using MessagePack.Formatters;

namespace ShaRPC.Serializers.MessagePack;

internal sealed class RpcStringFormatter : IMessagePackFormatter<string>
{
    public static readonly RpcStringFormatter Instance = new();

    private RpcStringFormatter()
    {
    }

    public void Serialize(
        ref MessagePackWriter writer,
        string value,
        MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        writer.Write(value);
    }

    public string Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null!;
        }

        return reader.TryReadStringSpan(out var utf8)
            ? RpcStringCache.GetOrAdd(utf8)
            : RpcStringCache.GetOrAdd(reader.ReadString()!);
    }
}
