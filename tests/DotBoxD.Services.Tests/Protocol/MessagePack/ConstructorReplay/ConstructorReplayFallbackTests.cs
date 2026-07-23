using System.Buffers;
using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using MessagePack.Formatters;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;

public sealed class ConstructorReplayFallbackTests
{
    [Fact]
    public void Complex_polymorphic_replay_compares_serialized_values_not_references()
    {
        var serializer = new MessagePackRpcSerializer();
        ComplexStableBaseDto value = new ComplexStableDto([1, 2, 3]);
        var writer = new ArrayBufferWriter<byte>();

        serializer.Serialize(writer, value);
        var roundTrip = serializer.Deserialize<ComplexStableBaseDto>(writer.WrittenMemory);

        Assert.Equal([1, 2, 3], roundTrip.Values);
        Assert.NotEqual(0, writer.WrittenCount);
        Assert.Null(ConstructorReplayTestSupport.GetValidator(value.GetType()));
    }

    [Fact]
    public void Complex_polymorphic_mismatch_fails_before_writing_destination()
    {
        var serializer = new MessagePackRpcSerializer();
        ComplexChangingBaseDto value = new ComplexChangingDto([1, 2], changesValues: true);
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(new byte[] { 0x91, 0x2a });
        var initialBytes = writer.WrittenSpan.ToArray();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, value));

        Assert.Contains(nameof(ComplexChangingDto), exception.Message);
        Assert.Equal(initialBytes, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Nonvisible_polymorphic_shape_remains_reflective_and_fails_closed()
    {
        var serializer = MessagePackRpcSerializer.CreateWithResolver(NonvisibleResolver.Instance);
        NonvisibleBaseDto stable = new NonvisibleDto(7, changesValue: false);
        var writer = ConstructorReplayTestSupport.Warm(serializer, stable);
        NonvisibleBaseDto changing = new NonvisibleDto(7, changesValue: true);
        var type = stable.GetType();
        var constructor = Assert.Single(type.GetConstructors());
        var properties = constructor.GetParameters()
            .Select(parameter => Assert.IsAssignableFrom<PropertyInfo>(type.GetProperty(
                parameter.Name!,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)))
            .ToArray();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, changing));

        Assert.False(ConstructorReplayValidatorCompiler.CanCompile(
            constructor,
            properties,
            properties));
        Assert.Null(ConstructorReplayTestSupport.GetValidator(type));
        Assert.Contains(nameof(NonvisibleDto), exception.Message);
    }

    public class ComplexStableBaseDto
    {
        public ComplexStableBaseDto(int[] values) => Values = values;

        public int[] Values { get; }
    }

    public sealed class ComplexStableDto : ComplexStableBaseDto
    {
        public ComplexStableDto(int[] values) : base(values.ToArray()) { }
    }

    public class ComplexChangingBaseDto
    {
        public ComplexChangingBaseDto(int[] values, bool changesValues)
        {
            Values = values;
            ChangesValues = changesValues;
        }

        public int[] Values { get; }

        public bool ChangesValues { get; }
    }

    public sealed class ComplexChangingDto : ComplexChangingBaseDto
    {
        public ComplexChangingDto(int[] values, bool changesValues)
            : base(changesValues ? [.. values, 1] : values.ToArray(), changesValues)
        {
        }
    }

    private class NonvisibleBaseDto
    {
        public NonvisibleBaseDto(int id, bool changesValue)
        {
            Id = id;
            ChangesValue = changesValue;
        }

        public int Id { get; }

        public bool ChangesValue { get; }
    }

    private sealed class NonvisibleDto : NonvisibleBaseDto
    {
        public NonvisibleDto(int id, bool changesValue)
            : base(changesValue ? id + 1 : id, changesValue)
        {
        }
    }

    private sealed class NonvisibleResolver : IFormatterResolver
    {
        public static readonly NonvisibleResolver Instance = new();

        public IMessagePackFormatter<T>? GetFormatter<T>() =>
            typeof(T) == typeof(NonvisibleBaseDto)
                ? (IMessagePackFormatter<T>)(object)NonvisibleFormatter.Instance
                : null;
    }

    private sealed class NonvisibleFormatter : IMessagePackFormatter<NonvisibleBaseDto>
    {
        public static readonly NonvisibleFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer,
            NonvisibleBaseDto value,
            MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(2);
            writer.WriteInt32(value.Id);
            writer.Write(value.ChangesValue);
        }

        public NonvisibleBaseDto Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            if (reader.ReadArrayHeader() != 2)
            {
                throw new MessagePackSerializationException("Invalid nonvisible replay DTO payload.");
            }

            return new NonvisibleBaseDto(reader.ReadInt32(), reader.ReadBoolean());
        }
    }
}
