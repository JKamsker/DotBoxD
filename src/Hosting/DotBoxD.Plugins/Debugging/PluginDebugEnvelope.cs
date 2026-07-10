using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotBoxD.Plugins.Debugging;

/// <summary>Versioned JSON envelope used by the generic remote-debug endpoints.</summary>
public sealed record PluginDebugEnvelope
{
    public PluginDebugEnvelope(
        int version,
        string kind,
        string id,
        string sessionToken,
        JsonElement payload)
    {
        Version = version;
        Kind = kind;
        Id = id;
        SessionToken = sessionToken;
        Payload = payload.Clone();
    }

    [JsonPropertyName("version")]
    public int Version { get; }

    [JsonPropertyName("kind")]
    public string Kind { get; }

    [JsonPropertyName("id")]
    public string Id { get; }

    [JsonPropertyName("sessionToken")]
    public string SessionToken { get; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; }
}
