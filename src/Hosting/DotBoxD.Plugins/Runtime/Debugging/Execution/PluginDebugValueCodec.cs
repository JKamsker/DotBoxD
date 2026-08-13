using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Plugins.Debugging;

internal static class PluginDebugValueCodec
{
    public static PluginDebugValueSnapshot Snapshot(SandboxValue value)
        => value switch
        {
            UnitValue => new(value.Type.ToString(), null),
            BoolValue scalar => new(value.Type.ToString(), scalar.Value),
            I32Value scalar => new(value.Type.ToString(), scalar.Value),
            I64Value scalar => new(value.Type.ToString(), scalar.Value),
            F64Value scalar => new(value.Type.ToString(), scalar.Value),
            StringValue scalar => new(value.Type.ToString(), scalar.Value),
            _ => SnapshotStructured(value)
        };

    private static PluginDebugValueSnapshot SnapshotStructured(SandboxValue value)
        => value switch
        {
            GuidValue scalar => new(value.Type.ToString(), scalar.Value.ToString("D")),
            OpaqueIdValue scalar => new(value.Type.ToString(), scalar.Value),
            SandboxPathValue scalar => new(value.Type.ToString(), scalar.Value.RelativePath),
            SandboxUriValue scalar => new(value.Type.ToString(), scalar.Value.Value),
            ListValue list => Collection(value.Type, list.Values),
            RecordValue record => Collection(value.Type, record.Fields),
            MapValue map => Map(map),
            _ => throw new NotSupportedException($"Unsupported sandbox debug value '{value.GetType().Name}'.")
        };

    public static bool TryParse(
        JsonElement element,
        SandboxType expectedType,
        out SandboxValue? value,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        try
        {
            value = Parse(element, expectedType);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            value = null;
            error = exception.Message;
            return false;
        }
    }

    private static PluginDebugValueSnapshot Collection(
        SandboxType type,
        IReadOnlyList<SandboxValue> values)
    {
        var children = new PluginDebugChildValue[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            children[index] = new PluginDebugChildValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture), Snapshot(values[index]));
        }

        return new PluginDebugValueSnapshot(type.ToString(), null, children);
    }

    private static PluginDebugValueSnapshot Map(MapValue map)
    {
        var entries = map.Values
            .Select(entry => new PluginDebugMapEntry(Snapshot(entry.Key), Snapshot(entry.Value)))
            .OrderBy(entry => JsonSerializer.Serialize(entry.Key), StringComparer.Ordinal)
            .ToArray();
        return new PluginDebugValueSnapshot(map.Type.ToString(), null, Entries: entries);
    }

    private static SandboxValue Parse(JsonElement element, SandboxType type)
    {
        RequireObject(element);
        return type.Name is "List" or SandboxType.RecordName or "Map"
            ? ParseStructured(element, type)
            : ParseScalar(element, type);
    }

    private static SandboxValue ParseScalar(JsonElement element, SandboxType type)
        => type.Name switch
        {
            "Unit" => SandboxValue.Unit,
            "Bool" => SandboxValue.FromBool(RequiredValue(element).GetBoolean()),
            "I32" => SandboxValue.FromInt32(RequiredValue(element).GetInt32()),
            "I64" => SandboxValue.FromInt64(RequiredValue(element).GetInt64()),
            "F64" => SandboxValue.FromDouble(RequiredValue(element).GetDouble()),
            "String" => SandboxValue.FromString(RequiredValue(element).GetString() ?? throw Invalid("String value is null.")),
            _ => ParseConstrainedScalar(element, type)
        };

    private static SandboxValue ParseConstrainedScalar(JsonElement element, SandboxType type)
        => type.Name switch
        {
            "Guid" => SandboxValue.FromGuid(Guid.Parse(RequiredValue(element).GetString() ?? string.Empty)),
            "SandboxPath" => SandboxValue.FromPath(RequiredValue(element).GetString() ?? string.Empty),
            "SandboxUri" => SandboxValue.FromUri(RequiredValue(element).GetString() ?? string.Empty),
            _ when type.Arguments.Count == 0 => SandboxValue.FromOpaqueId(
                type.Name,
                RequiredValue(element).GetString() ?? string.Empty),
            _ => throw Invalid($"Unsupported sandbox type '{type}'.")
        };

    private static SandboxValue ParseStructured(JsonElement element, SandboxType type)
        => type.Name switch
        {
            "List" => ParseList(element, type),
            SandboxType.RecordName => ParseRecord(element, type),
            "Map" => ParseMap(element, type),
            _ => throw Invalid($"Unsupported sandbox type '{type}'.")
        };

    private static SandboxValue ParseList(JsonElement element, SandboxType type)
    {
        if (type.Arguments.Count != 1)
        {
            throw Invalid("List type is malformed.");
        }

        var children = RequiredArray(element, "children");
        var values = children.EnumerateArray()
            .Select(child => Parse(RequiredProperty(child, "value"), type.Arguments[0]))
            .ToArray();
        return SandboxValue.FromList(values, type.Arguments[0]);
    }

    private static SandboxValue ParseRecord(JsonElement element, SandboxType type)
    {
        var children = RequiredArray(element, "children").EnumerateArray().ToArray();
        if (children.Length != type.Arguments.Count)
        {
            throw Invalid("Record field count does not match the variable type.");
        }

        var values = new SandboxValue[children.Length];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = Parse(RequiredProperty(children[index], "value"), type.Arguments[index]);
        }

        return SandboxValue.FromRecord(values);
    }

    private static SandboxValue ParseMap(JsonElement element, SandboxType type)
    {
        if (type.Arguments.Count != 2)
        {
            throw Invalid("Map type is malformed.");
        }

        var values = new Dictionary<SandboxValue, SandboxValue>();
        foreach (var entry in RequiredArray(element, "entries").EnumerateArray())
        {
            var key = Parse(RequiredProperty(entry, "key"), type.Arguments[0]);
            var value = Parse(RequiredProperty(entry, "value"), type.Arguments[1]);
            if (!values.TryAdd(key, value))
            {
                throw Invalid("Map replacement contains duplicate keys.");
            }
        }

        return SandboxValue.FromMap(values, type.Arguments[0], type.Arguments[1]);
    }

    private static JsonElement RequiredValue(JsonElement element) => RequiredProperty(element, "value");

    private static JsonElement RequiredArray(JsonElement element, string name)
    {
        var value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"Debug value {name} must be an array.");
        }

        return value;
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
    {
        RequireObject(element);
        if (!element.TryGetProperty(name, out var value))
        {
            throw Invalid($"Debug value {name} is required.");
        }

        return value;
    }

    private static void RequireObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Debug values must be JSON objects.");
        }
    }

    private static ArgumentException Invalid(string message) => new(message);
}

internal sealed record PluginDebugValueSnapshot(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Value,
    [property: JsonPropertyName("children"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PluginDebugChildValue>? Children = null,
    [property: JsonPropertyName("entries"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PluginDebugMapEntry>? Entries = null);

internal sealed record PluginDebugChildValue(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] PluginDebugValueSnapshot Value);

internal sealed record PluginDebugMapEntry(
    [property: JsonPropertyName("key")] PluginDebugValueSnapshot Key,
    [property: JsonPropertyName("value")] PluginDebugValueSnapshot Value);
