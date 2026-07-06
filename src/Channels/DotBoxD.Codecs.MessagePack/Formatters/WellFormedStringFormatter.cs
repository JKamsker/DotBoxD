using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class WellFormedStringResolver : IFormatterResolver
{
    public static readonly WellFormedStringResolver Instance = new();

    public IMessagePackFormatter<T>? GetFormatter<T>() =>
        typeof(T) == typeof(string)
            ? (IMessagePackFormatter<T>)(object)WellFormedStringFormatter.Instance
            : null;
}

internal sealed class WellFormedStringFormatter : IMessagePackFormatter<string?>
{
    public static readonly WellFormedStringFormatter Instance = new();

    public void Serialize(
        ref MessagePackWriter writer,
        string? value,
        MessagePackSerializerOptions options)
    {
        RequireWellFormedUtf16(value);
        writer.Write(value);
    }

    public string? Deserialize(
        ref MessagePackReader reader,
        MessagePackSerializerOptions options) =>
        reader.ReadString();

    private static void RequireWellFormedUtf16(string? value)
    {
        if (value is null)
        {
            return;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsHighSurrogate(current))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                throw MalformedUtf16();
            }

            if (char.IsLowSurrogate(current))
            {
                throw MalformedUtf16();
            }
        }
    }

    private static MessagePackSerializationException MalformedUtf16() =>
        new("String payload contains malformed UTF-16 text with an unpaired surrogate.");
}
