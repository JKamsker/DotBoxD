using System.Text;
using DotBoxD.Services.Protocol;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class RpcStreamHandleFormatter : IMessagePackFormatter<RpcStreamHandle>
{
    public static readonly RpcStreamHandleFormatter Instance = new();

    private static readonly byte[] StreamIdKey = Encoding.UTF8.GetBytes("StreamId");
    private static readonly byte[] KindKey = Encoding.UTF8.GetBytes("Kind");

    private RpcStreamHandleFormatter()
    {
    }

    public void Serialize(
        ref MessagePackWriter writer,
        RpcStreamHandle value,
        MessagePackSerializerOptions options)
    {
        writer.WriteMapHeader(2);
        writer.WriteString(StreamIdKey);
        writer.Write(value.StreamId);
        writer.WriteString(KindKey);
        writer.Write((byte)value.Kind);
    }

    public RpcStreamHandle Deserialize(
        ref MessagePackReader reader,
        MessagePackSerializerOptions options)
    {
        options.Security.DepthStep(ref reader);
        try
        {
            var count = reader.ReadMapHeader();
            var streamId = 0;
            var kind = default(RpcStreamKind);

            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                switch (name)
                {
                    case "StreamId":
                        streamId = reader.ReadInt32();
                        break;
                    case "Kind":
                        kind = (RpcStreamKind)reader.ReadByte();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new RpcStreamHandle(streamId, kind);
        }
        finally
        {
            reader.Depth--;
        }
    }
}
