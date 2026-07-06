using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class RpcObjectFormatter : IMessagePackFormatter<object?>
{
    public static readonly RpcObjectFormatter Instance = new();

    private static readonly IMessagePackFormatter<object?> PrimitiveFormatter =
        PrimitiveObjectResolver.Instance.GetFormatter<object?>()
        ?? throw new MessagePackSerializationException("No primitive object formatter is registered.");

    private static readonly HashSet<Type> SupportedScalarTypes = new()
    {
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(string),
    };

    private RpcObjectFormatter()
    {
    }

    public void Serialize(ref MessagePackWriter writer, object? value, MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        ThrowIfUnsupported(value.GetType());
        PrimitiveFormatter.Serialize(ref writer, value, options);
    }

    public object? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var value = PrimitiveFormatter.Deserialize(ref reader, options);
        if (value is null)
        {
            return null;
        }

        ThrowIfUnsupported(value.GetType());
        return value;
    }

    private static void ThrowIfUnsupported(Type type)
    {
        if (IsSupportedScalar(type))
        {
            return;
        }

        throw new MessagePackSerializationException(
            "Object-declared MessagePack RPC payloads only support null and primitive scalar values. " +
            "Declare enum, collection, tuple, and aggregate payloads with their concrete type.");
    }

    private static bool IsSupportedScalar(Type type) => SupportedScalarTypes.Contains(type);
}
