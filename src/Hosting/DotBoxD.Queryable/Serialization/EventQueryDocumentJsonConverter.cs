using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Serializes an <see cref="EventQueryDocument"/> as a versioned envelope
/// (<c>{ "version", "event", "filter", "projection" }</c>). The version is written for forward
/// compatibility and tolerated (any value, or absent) on read so older logs and caches stay loadable.
/// </summary>
public sealed class EventQueryDocumentJsonConverter : JsonConverter<EventQueryDocument>
{
    private const int CurrentVersion = 1;

    /// <inheritdoc />
    public override EventQueryDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var eventName = root.TryGetProperty("event", out var e) && e.GetString() is { } name
            ? name
            : throw new JsonException("Event query document is missing required property 'event'.");

        var filter = root.TryGetProperty("filter", out var f)
            ? f.Deserialize<QueryFilter>(options) ?? QueryFilter.MatchAll
            : QueryFilter.MatchAll;

        var projection = root.TryGetProperty("projection", out var p)
            ? p.Deserialize<QueryProjection>(options) ?? QueryProjection.Identity
            : QueryProjection.Identity;

        return EventQueryDocument.Create(eventName, filter, projection);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, EventQueryDocument value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        writer.WriteStartObject();
        writer.WriteNumber("version", CurrentVersion);
        writer.WriteString("event", value.EventName);
        writer.WritePropertyName("filter");
        JsonSerializer.Serialize(writer, value.Filter, options);
        writer.WritePropertyName("projection");
        JsonSerializer.Serialize(writer, value.Projection, options);
        writer.WriteEndObject();
    }
}
