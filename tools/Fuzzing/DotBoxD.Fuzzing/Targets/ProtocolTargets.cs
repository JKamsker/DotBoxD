using System.Buffers;
using System.Buffers.Binary;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;

namespace DotBoxD.Fuzzing.Targets;

internal static class ProtocolTargets
{
    private static readonly MessagePackRpcSerializer Serializer = new();

    public static void Frame(byte[] bytes)
    {
        var headerValid = bytes.Length >= 9 && bytes.Length <= 16 * 1024 * 1024
            && BinaryPrimitives.ReadInt32LittleEndian(bytes) == bytes.Length
            && bytes[8] is >= 1 and <= 9;
        Require(headerValid == MessageFramer.TryReadFrameHeader(bytes, out var headerId, out var headerType));
        if (headerValid)
        {
            Require(headerId == BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) && (byte)headerType == bytes[8]);
        }

        var size = bytes.Length >= 13 ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(9)) : -1;
        var expected = headerValid && bytes.Length >= 13 && size >= 0 && size <= bytes.Length - 13;
        Require(expected == MessageFramer.TryReadFrame(bytes, out var id, out var type, out var envelope, out var payload));
        if (expected)
        {
            Require(id == headerId && type == headerType);
            Require(envelope.Span.SequenceEqual(bytes.AsSpan(13, size)));
            Require(payload.Span.SequenceEqual(bytes.AsSpan(13 + size)));
        }
        else
        {
            Require(envelope.IsEmpty && payload.IsEmpty);
        }
    }

    public static void Request(byte[] bytes) => Envelope<RpcRequest>(bytes);

    public static void Response(byte[] bytes) => Envelope<RpcResponse>(bytes);

    private static void Envelope<T>(byte[] bytes)
    {
        T value;
        try
        {
            value = Serializer.Deserialize<T>(bytes);
        }
        catch (MessagePackSerializationException)
        {
            // Both public deserialization paths must agree on acceptance.
            try
            {
                Serializer.Deserialize(bytes, typeof(T));
            }
            catch (MessagePackSerializationException)
            {
                return;
            }

            throw new InvalidOperationException("Generic and runtime-type deserializers disagree on rejection.");
        }

        // Successful decoding must produce a stable, serializable envelope.
        var canonical = Serialize(value);
        Require(canonical.AsSpan().SequenceEqual(Serialize(Serializer.Deserialize<T>(canonical))));
        var runtimeValue = (T)Serializer.Deserialize(bytes, typeof(T))!;
        Require(canonical.AsSpan().SequenceEqual(Serialize(runtimeValue)));
    }

    private static byte[] Serialize<T>(T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        Serializer.Serialize(writer, value);
        return writer.WrittenSpan.ToArray();
    }

    private static void Require(bool condition)
    {
        if (!condition)
        { throw new InvalidOperationException("Protocol fuzz invariant failed."); }
    }
}
