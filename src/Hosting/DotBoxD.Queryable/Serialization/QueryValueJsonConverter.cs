using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Serializes a <see cref="QueryValue"/> as a raw JSON scalar (<c>"player-1"</c>, <c>5</c>, <c>true</c>,
/// <c>null</c>) so a host sees ordinary literals rather than a tagged wrapper, and reads it back by JSON
/// token type. Integral numbers map to <see cref="QueryValueKind.Integer"/> and fractional numbers to
/// <see cref="QueryValueKind.Number"/>. Capture provenance (<see cref="QueryValue.ParameterKey"/>) is
/// runtime-only and is not part of the wire form.
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
            JsonTokenType.String => QueryValue.FromString(reader.GetString()),
            JsonTokenType.Number => ReadNumber(ref reader),
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
                writer.WriteNumberValue(value.Number);
                break;
            case QueryValueKind.String:
                writer.WriteStringValue(value.String);
                break;
            default:
                throw new JsonException($"Unsupported query value kind '{value.Kind}'.");
        }
    }

    private static QueryValue ReadNumber(ref Utf8JsonReader reader) =>
        reader.TryGetInt64(out var integer)
            ? QueryValue.FromInteger(integer)
            : QueryValue.FromNumber(reader.GetDouble());
}
