using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// The JSON entry point for the portable query model. Provides the shared, deterministic
/// <see cref="Options"/> used to read and write <see cref="EventQueryDocument"/> and its sub-trees, and
/// convenience round-trip helpers. The serialized form is stable and suitable for logs, caches, and tests.
/// </summary>
public static class EventQueryJson
{
    /// <summary>
    /// The shared serializer options: short enum tokens, raw scalar values, and compact tagged-union
    /// filter/projection documents. Property order is deterministic (member declaration order).
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions(indented: false);

    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(indented: true);

    /// <summary>Serializes a document to its canonical JSON wire form.</summary>
    public static string Serialize(EventQueryDocument document, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, indented ? IndentedOptions : Options);
    }

    /// <summary>Deserializes a document from its JSON wire form.</summary>
    public static EventQueryDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return JsonSerializer.Deserialize<EventQueryDocument>(json, Options)
            ?? throw new JsonException("Event query document JSON deserialized to null.");
    }

    private static JsonSerializerOptions CreateOptions(bool indented) => new()
    {
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
            new QueryValueJsonConverter(),
            new QueryFilterJsonConverter(),
            new QueryProjectionJsonConverter(),
            new EventQueryDocumentJsonConverter(),
        },
    };
}
