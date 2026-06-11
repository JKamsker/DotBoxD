using System.Text.Json;

namespace SafeIR;

internal static class JsonImport
{
    public static readonly SourceSpan JsonSpan = new(0, 0);

    public static JsonElement Required(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value
            : throw Error("E-JSON-MISSING", $"missing required property '{name}'");

    public static JsonElement RequiredArray(JsonElement element, string name)
    {
        var value = Required(element, name);
        RequireArray(value, name);
        return value;
    }

    public static string RequiredString(JsonElement element, string name)
        => ReadStringValue(Required(element, name), name);

    public static string? OptionalString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? ReadStringValue(value, name) : null;

    public static string ReadStringValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String) {
            throw Error("E-JSON-TYPE", $"'{name}' must be a string");
        }

        return value.GetString() ?? "";
    }

    public static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) {
            throw Error("E-JSON-TYPE", $"{name} must be an object");
        }
    }

    public static void RequireArray(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array) {
            throw Error("E-JSON-TYPE", $"{name} must be an array");
        }
    }

    public static void RequireAllowedProperties(JsonElement value, string name, params string[] allowed)
    {
        RequireObject(value, name);
        foreach (var property in value.EnumerateObject()) {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) {
                throw Error("E-JSON-SCHEMA", $"{name} contains unsupported property '{property.Name}'");
            }
        }
    }

    public static SandboxValidationException Error(string code, string message)
        => new([new SandboxDiagnostic(code, message, Span: JsonSpan)]);
}
