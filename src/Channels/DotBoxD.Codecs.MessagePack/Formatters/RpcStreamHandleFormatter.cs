using System.Text;
using DotBoxD.Services.Protocol;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class RpcStreamHandleFormatter : IMessagePackFormatter<RpcStreamHandle>
{
    /// <summary>
    /// Smallest possible encoding of a valid handle: a fixmap header (1), the "StreamId" fixstr key (9)
    /// with a positive fixint value (1), and the "Kind" fixstr key (5) with a fixint value (1). Both
    /// fields are required, so no valid handle is shorter.
    /// </summary>
    internal const int MinimumEncodedSize = 17;

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
            var seenStreamId = false;
            var seenKind = false;

            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                switch (name)
                {
                    case "StreamId":
                        ThrowIfDuplicate(seenStreamId, nameof(RpcStreamHandle.StreamId));
                        seenStreamId = true;
                        streamId = reader.ReadInt32();
                        break;
                    case "Kind":
                        ThrowIfDuplicate(seenKind, nameof(RpcStreamHandle.Kind));
                        seenKind = true;
                        kind = (RpcStreamKind)reader.ReadByte();
                        break;
                    default:
                        MessagePackEnvelopeSkipper.SkipUnknownField(ref reader, "RPC stream handle");
                        break;
                }
            }

            ThrowIfMissing(seenStreamId, nameof(RpcStreamHandle.StreamId));
            ThrowIfMissing(seenKind, nameof(RpcStreamHandle.Kind));
            return new RpcStreamHandle(streamId, kind);
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static void ThrowIfDuplicate(bool alreadySeen, string fieldName)
    {
        if (alreadySeen)
        {
            throw new RpcEnvelopeValidationException(
                $"RPC stream handle contains duplicate {fieldName}.");
        }
    }

    private static void ThrowIfMissing(bool seen, string fieldName)
    {
        if (!seen)
        {
            throw new RpcEnvelopeValidationException(
                $"RPC stream handle is missing required {fieldName}.");
        }
    }
}
