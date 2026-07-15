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
            path.Add(ParseSegment(item, current, out current));
        }

        return path;
    }

    private static SandboxDebugValuePathSegment ParseSegment(
        JsonElement item,
        SandboxType current,
        out SandboxType next)
        => RequiredString(item, "kind") switch
        {
            "list" => ParseList(item, current, out next),
            "record" => ParseRecord(item, current, out next),
            "map" => ParseMap(item, current, out next),
            var kind => throw new ArgumentException($"Unknown debug value path kind '{kind}'.")
        };

    private static SandboxDebugValuePathSegment ParseList(JsonElement item, SandboxType current, out SandboxType next)
    {
        if (current.Name != "List" || current.Arguments.Count != 1)
        {
            throw Mismatch("list", current);
        }

        next = current.Arguments[0];
        return new SandboxDebugListIndex(RequiredIndex(item));
    }

    private static SandboxDebugValuePathSegment ParseRecord(JsonElement item, SandboxType current, out SandboxType next)
    {
        if (current.Name != SandboxType.RecordName)
        {
            throw Mismatch("record", current);
        }

        var index = RequiredIndex(item);
        if (index >= current.Arguments.Count)
        {
            throw new ArgumentException("Debug record field is out of range.");
        }

        next = current.Arguments[index];
        return new SandboxDebugRecordField(index);
    }

    private static SandboxDebugValuePathSegment ParseMap(JsonElement item, SandboxType current, out SandboxType next)
    {
        if (current.Name != "Map" || current.Arguments.Count != 2)
        {
            throw Mismatch("map", current);
        }

        SandboxValue? parsed = null;
        string? error = null;
        if (!item.TryGetProperty("key", out var key) ||
            !PluginDebugValueCodec.TryParse(key, current.Arguments[0], out parsed, out error))
        {
            throw new ArgumentException(error ?? "Debug map key is required.");
        }

        next = current.Arguments[1];
        return new SandboxDebugMapValue(parsed!);
    }

    private static ArgumentException Mismatch(string kind, SandboxType current)
        => new($"Debug value path kind '{kind}' does not match '{current}'.");

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
