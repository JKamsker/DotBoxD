using System.Text.Json.Serialization;

namespace DotBoxD.UI;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(UiPackage))]
internal sealed partial class UiJsonContext : JsonSerializerContext;
