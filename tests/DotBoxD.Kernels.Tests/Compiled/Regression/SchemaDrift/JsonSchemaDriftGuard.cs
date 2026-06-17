using System.Text.Json;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

internal static class JsonSchemaDriftGuard
{
    public static IReadOnlyList<string> SemanticDriftMessages(
        string schemaJson,
        JsonSchemaObjectContract contract)
    {
        using var document = JsonDocument.Parse(schemaJson);
        var matchedProperties = FindMatchingPropertySet(document.RootElement, contract.AllowedProperties);
        return matchedProperties is null
            ? [$"{contract.Name} property set is missing from the schema."]
            : [];
    }

    private static IReadOnlyList<string>? FindMatchingPropertySet(JsonElement element, string[] expected)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("properties", out var properties) &&
                    properties.ValueKind == JsonValueKind.Object)
                {
                    var names = properties.EnumerateObject().Select(p => p.Name).ToArray();
                    if (SameSet(names, expected))
                    {
                        return names;
                    }
                }

                foreach (var child in element.EnumerateObject())
                {
                    var found = FindMatchingPropertySet(child.Value, expected);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindMatchingPropertySet(item, expected);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static bool SameSet(IEnumerable<string> left, IEnumerable<string> right)
        => new HashSet<string>(left, StringComparer.Ordinal)
            .SetEquals(new HashSet<string>(right, StringComparer.Ordinal));
}
