using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Serializes a <see cref="QueryValue"/>. The original scalar kinds are written as raw JSON scalars when the
/// scalar token can round-trip the kind. Integral-valued doubles are tagged because a bare <c>1</c> is
/// indistinguishable from an integer on read. The exact kinds added later (Guid, Decimal, UnsignedInteger,
/// Timestamp) also use a tagged object <c>{"kind":"…","value":"…"}</c> with the value as a canonical string.
/// Capture provenance (<see cref="QueryValue.ParameterKey"/>) is runtime-only and is not part of the wire form.
/// </summary>
public sealed class QueryValueJsonConverter : JsonConverter<QueryValue>
{
    /// <inheritdoc />
    public override QueryValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => QueryValue.Null,
            JsonTokenType.True => QueryValue.FromBoolean(true),
            JsonTokenType.False => QueryValue.FromBoolean(false),
            JsonTokenType.String => QueryValue.FromString(ReadString(ref reader, "query value")),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.StartObject => ReadTagged(ref reader),
            _ => throw new JsonException($"Unsupported JSON token '{reader.TokenType}' for a query value."),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, QueryValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        switch (value.Kind)
        {
            case QueryValueKind.Null:
                writer.WriteNullValue();
                break;
            case QueryValueKind.Boolean:
                writer.WriteBooleanValue(value.Boolean);
                break;
            case QueryValueKind.Integer:
                writer.WriteNumberValue(value.Integer);
                break;
            case QueryValueKind.Number:
                WriteNumber(writer, value.Number);
                break;
            case QueryValueKind.String:
                WriteStringValue(writer, value.String);
                break;
            default:
                WriteTaggedOrThrow(writer, value);
                break;
        }
    }

    private static void WriteTaggedOrThrow(Utf8JsonWriter writer, QueryValue value)
    {
        switch (value.Kind)
        {
            case QueryValueKind.Guid:
                WriteTagged(writer, "guid", value.Guid.ToString("D"));
                break;
            case QueryValueKind.Decimal:
                WriteTagged(writer, "decimal", QueryValue.CanonicalDecimal(value.Decimal));
                break;
            case QueryValueKind.UnsignedInteger:
                WriteTagged(writer, "ulong", value.UnsignedInteger.ToString(CultureInfo.InvariantCulture));
                break;
            case QueryValueKind.Timestamp:
                WriteTagged(writer, "timestamp", QueryValue.CanonicalTimestamp(value.Timestamp));
                break;
            default:
                throw new JsonException($"Unsupported query value kind '{value.Kind}'.");
        }
    }

    private static void WriteTagged(Utf8JsonWriter writer, string kind, string value)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        writer.WriteString("value", value);
        writer.WriteEndObject();
    }

    private static void WriteNumber(Utf8JsonWriter writer, double value)
    {
        if (value == Math.Truncate(value))
        {
            WriteTagged(writer, "number", value.ToString("R", CultureInfo.InvariantCulture));
            return;
        }

        writer.WriteNumberValue(value);
    }

    private static string? ReadString(ref Utf8JsonReader reader, string name)
    {
        QueryValueJsonUtf16Escapes.RejectMalformedEscapedUtf16(ref reader, name);
        var value = reader.GetString();
        return RequireWellFormedUtf16(value, name);
    }

    private static void WriteStringValue(Utf8JsonWriter writer, string? value)
    {
        writer.WriteStringValue(RequireWellFormedUtf16(value, "query value"));
    }

    private static QueryValue ReadNumber(ref Utf8JsonReader reader) =>
        reader.TryGetInt64(out var integer)
            ? QueryValue.FromInteger(integer)
            : QueryValue.FromNumber(reader.GetDouble());

    // A value position never holds a filter/projection object (those have their own converters), so a
    // StartObject here is unambiguously a tagged exact-kind value.
    private static QueryValue ReadTagged(ref Utf8JsonReader reader)
    {
        var (kind, text) = ReadTaggedFields(ref reader);
        if (text is null)
        {
            throw new JsonException("A tagged query value is missing its 'value'.");
        }

        return ReadTaggedValue(kind, text);
    }

    private static (string? Kind, string? Text) ReadTaggedFields(ref Utf8JsonReader reader)
    {
        string? kind = null;
        string? text = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var property = reader.GetString();
            reader.Read();
            if (property == "kind")
            {
                kind = ReadString(ref reader, "tagged query value kind");
            }
            else if (property == "value")
            {
                text = ReadString(ref reader, "tagged query value");
            }
        }

        return (kind, text);
    }

    private static QueryValue ReadTaggedValue(string? kind, string text)
        => kind switch
        {
            "number" => ReadTaggedNumber(kind, text),
            "guid" => ReadGuid(kind, text),
            "decimal" => ReadDecimal(kind, text),
            "ulong" => ReadUnsignedInteger(kind, text),
            "timestamp" => ReadTimestamp(kind, text),
            _ => throw new JsonException($"Unknown tagged query value kind '{kind}'."),
        };

    private static QueryValue ReadGuid(string kind, string text) =>
        Guid.TryParse(text, out var value)
            ? QueryValue.FromGuid(value)
            : throw InvalidTaggedValue(kind, text);

    private static QueryValue ReadTaggedNumber(string kind, string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? QueryValue.FromNumber(value)
            : throw InvalidTaggedValue(kind, text);

    private static QueryValue ReadDecimal(string kind, string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? QueryValue.FromDecimal(value)
            : throw InvalidTaggedValue(kind, text);

    private static QueryValue ReadUnsignedInteger(string kind, string text) =>
        ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? QueryValue.FromUnsignedInteger(value)
            : throw InvalidTaggedValue(kind, text);

    private static QueryValue ReadTimestamp(string kind, string text)
    {
        if (!QueryValue.HasExplicitTimestampOffset(text))
        {
            throw new JsonException($"Timestamp '{text}' must include an explicit UTC 'Z' or +/-hh:mm offset.");
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? QueryValue.FromTimestamp(value)
            : throw InvalidTaggedValue(kind, text);
    }

    private static JsonException InvalidTaggedValue(string kind, string text) =>
        new($"Invalid tagged query value '{text}' for kind '{kind}'.");

    private static string? RequireWellFormedUtf16(string? value, string name) =>
        value is null ? null : EventQueryJsonStringSafety.RequireWellFormedUtf16(value, name);

}
