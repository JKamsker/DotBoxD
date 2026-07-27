using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.AotSmoke;

public static class AotConstructorReplaySmoke
{
    // Cross the runtime's hot-validator admission threshold. NativeAOT must keep using the
    // reflection path because dynamic code compilation is unavailable there.
    private const int ReplayCount = 8193;

    // A generated/static formatter does not itself promise reflection metadata. Root this smoke's
    // public replay shape explicitly so NativeAOT exercises the reflective validator rather than
    // trimming the constructor/properties and bypassing the guard as an unsupported shape.
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(ReplayBaseDto))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(ReplayDerivedDto))]
    public static bool Run()
    {
        var serializer = MessagePackRpcSerializer.CreateWithResolver(ReplayResolver.Instance);
        var value = new ReplayDerivedDto(42, changesValue: false);
        ReplayBaseDto asBase = value;
        var writer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < ReplayCount; i++)
        {
            serializer.Serialize(writer, asBase);
            writer.Clear();
        }

        serializer.Serialize(writer, value);
        var exactRoundTrip = serializer.Deserialize<ReplayDerivedDto>(writer.WrittenMemory);
        writer.Clear();
        writer.Write(new byte[] { 0x91, 0x2a });
        var initialBytes = writer.WrittenSpan.ToArray();
        var changingValueFailed = FailsWithoutWriting(
            serializer,
            writer,
            new ReplayDerivedDto(42, changesValue: true),
            initialBytes);
        return exactRoundTrip.Id == value.Id && changingValueFailed;
    }

    private static bool FailsWithoutWriting(
        MessagePackRpcSerializer serializer,
        ArrayBufferWriter<byte> writer,
        ReplayDerivedDto value,
        byte[] initialBytes)
    {
        try
        {
            serializer.Serialize(writer, value);
            return false;
        }
        catch (MessagePackSerializationException exception)
        {
            return exception.Message.Contains(nameof(ReplayDerivedDto), StringComparison.Ordinal) &&
                writer.WrittenSpan.SequenceEqual(initialBytes);
        }
    }

    public class ReplayBaseDto
    {
        public ReplayBaseDto(int id, bool changesValue)
        {
            Id = id;
            ChangesValue = changesValue;
        }

        public int Id { get; }

        public bool ChangesValue { get; }
    }

    public sealed class ReplayDerivedDto : ReplayBaseDto
    {
        public ReplayDerivedDto(int id, bool changesValue)
            : base(changesValue ? id + 1 : id, changesValue)
        {
        }
    }

    private sealed class ReplayResolver : IFormatterResolver
    {
        public static readonly ReplayResolver Instance = new();

        public IMessagePackFormatter<T>? GetFormatter<T>() =>
            typeof(T) == typeof(ReplayBaseDto)
                ? (IMessagePackFormatter<T>)(object)ReplayBaseFormatter.Instance
                : typeof(T) == typeof(ReplayDerivedDto)
                    ? (IMessagePackFormatter<T>)(object)ReplayDerivedFormatter.Instance
                    : null;
    }

    private sealed class ReplayBaseFormatter : IMessagePackFormatter<ReplayBaseDto>
    {
        public static readonly ReplayBaseFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer,
            ReplayBaseDto value,
            MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(2);
            writer.WriteInt32(value.Id);
            writer.Write(value.ChangesValue);
        }

        public ReplayBaseDto Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options) =>
            Read(ref reader, static (id, changesValue) => new ReplayBaseDto(id, changesValue));
    }

    private sealed class ReplayDerivedFormatter : IMessagePackFormatter<ReplayDerivedDto>
    {
        public static readonly ReplayDerivedFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer,
            ReplayDerivedDto value,
            MessagePackSerializerOptions options) =>
            ReplayBaseFormatter.Instance.Serialize(ref writer, value, options);

        public ReplayDerivedDto Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options) =>
            Read(ref reader, static (id, changesValue) => new ReplayDerivedDto(id, changesValue));
    }

    private static T Read<T>(
        ref MessagePackReader reader,
        Func<int, bool, T> create)
    {
        if (reader.ReadArrayHeader() != 2)
        {
            throw new MessagePackSerializationException("Invalid constructor replay DTO payload.");
        }

        return create(reader.ReadInt32(), reader.ReadBoolean());
    }
}
