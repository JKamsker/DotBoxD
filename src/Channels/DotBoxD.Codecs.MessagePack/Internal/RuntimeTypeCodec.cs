using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using MessagePack;

namespace DotBoxD.Codecs.MessagePack;

// MessagePack's Type-based overloads cache closed generic delegates process-wide. Keep ours weak
// and call the generic overloads so an otherwise unused collectible DTO can be reclaimed.
internal sealed class RuntimeTypeCodec
{
    private delegate object? DeserializeDelegate(ref MessagePackReader reader, MessagePackSerializerOptions options);
    private delegate void SerializeDelegate(IBufferWriter<byte> writer, object? value, MessagePackSerializerOptions options);

    private static readonly ConditionalWeakTable<Type, RuntimeTypeCodec> Codecs = new();
    private static readonly MethodInfo DeserializeMethod = typeof(RuntimeTypeCodec)
        .GetMethod(nameof(DeserializeTyped), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo SerializeMethod = typeof(RuntimeTypeCodec)
        .GetMethod(nameof(SerializeTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly DeserializeDelegate _deserialize;
    private readonly SerializeDelegate _serialize;

    [RequiresUnreferencedCode("Runtime-type serialization requires preserved generic codec instantiations and DTO formatter metadata.")]
    private RuntimeTypeCodec(Type type)
    {
        _deserialize = (DeserializeDelegate)DeserializeMethod.MakeGenericMethod(type)
            .CreateDelegate(typeof(DeserializeDelegate));
        _serialize = (SerializeDelegate)SerializeMethod.MakeGenericMethod(type)
            .CreateDelegate(typeof(SerializeDelegate));
    }

    public static RuntimeTypeCodec For(Type type) => Codecs.GetValue(type, static key => new RuntimeTypeCodec(key));

    public object? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        _deserialize(ref reader, options);

    public void Serialize(IBufferWriter<byte> writer, object? value, MessagePackSerializerOptions options) =>
        _serialize(writer, value, options);

    private static object? DeserializeTyped<T>(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        MessagePackSerializer.Deserialize<T>(ref reader, options);

    private static void SerializeTyped<T>(IBufferWriter<byte> writer, object? value, MessagePackSerializerOptions options) =>
        MessagePackSerializer.Serialize(writer, (T)value!, options);
}
