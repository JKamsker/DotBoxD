using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotBoxD.Plugins.Packaging;

/// <summary>A portable signed envelope. The exact UTF-8 package text and all identity fields are authenticated.</summary>
public sealed record PluginArtifact(
    int SchemaVersion,
    string PluginId,
    string Version,
    string Publisher,
    string PackageJson,
    string ContentHash,
    string? KeyId,
    string? Signature)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumJsonLength = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 64
    };

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static PluginArtifact Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumJsonLength)
        {
            throw new InvalidDataException("Plugin artifact exceeds the size limit.");
        }
        return JsonSerializer.Deserialize<PluginArtifact>(json, Options)
            ?? throw new InvalidDataException("Expected a plugin artifact.");
    }
}
