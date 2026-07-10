using System.Text.Json;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Debugging;

internal static class PluginDebugValuePathParser
{
    public static IReadOnlyList<SandboxDebugValuePathSegment> Parse(JsonElement payload, SandboxType rootType)
    {
        if (!payload.TryGetProperty("path", out var encoded) || encoded.ValueKind is JsonValueKind.Null)
        {
            return [];
        }

        if (encoded.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Debug value path must be an array.");
        }

        var current = rootType;
        var path = new List<SandboxDebugValuePathSegment>();
        foreach (var item in encoded.EnumerateArray())
        {
            var kind = RequiredString(item, "kind");
            switch (kind)
            {
                case "list" when current.Name == "List" && current.Arguments.Count == 1:
                    path.Add(new SandboxDebugListIndex(RequiredIndex(item)));
                    current = current.Arguments[0];
                    break;
                case "record" when current.Name == SandboxType.RecordName:
                    var index = RequiredIndex(item);
                    if (index >= current.Arguments.Count)
                    {
                        throw new ArgumentException("Debug record field is out of range.");
                    }

                    path.Add(new SandboxDebugRecordField(index));
                    current = current.Arguments[index];
                    break;
                case "map" when current.Name == "Map" && current.Arguments.Count == 2:
                    if (!item.TryGetProperty("key", out var key))
                    {
                        throw new ArgumentException("Debug map key is required.");
                    }

                    if (!PluginDebugValueCodec.TryParse(key, current.Arguments[0], out var parsed, out var error))
                    {
                        throw new ArgumentException(error);
                    }

                    path.Add(new SandboxDebugMapValue(parsed!));
                    current = current.Arguments[1];
                    break;
                default:
                    throw new ArgumentException($"Debug value path kind '{kind}' does not match '{current}'.");
            }
        }

        return path;
    }

    private static int RequiredIndex(JsonElement value)
        => value.TryGetProperty("index", out var index) && index.TryGetInt32(out var parsed) && parsed >= 0
            ? parsed
            : throw new ArgumentException("Debug value path index must be a non-negative integer.");

    private static string RequiredString(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object &&
           value.TryGetProperty(name, out var property) &&
           property.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new ArgumentException($"Debug value path {name} is required.");
}
