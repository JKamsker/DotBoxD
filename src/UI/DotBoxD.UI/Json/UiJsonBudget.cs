using System.Text.Json;

namespace DotBoxD.UI;

internal static class UiJsonBudget
{
    public static void Validate(JsonElement root, UiPolicy policy)
    {
        CheckFeatures(root);
        CheckOptionalCollections(root, policy);
        foreach (var slot in RequireArray(root, "state", policy.MaxStateSlots).EnumerateArray())
        {
            if (slot.ValueKind != JsonValueKind.Object || !slot.TryGetProperty("initialValue", out var value))
            { throw new UiValidationException("Missing UI initial state value."); }
            CheckValue(value, policy);
        }
        RequireArray(root, "kernels", policy.MaxKernels);
        RequireArray(root, "events", policy.MaxEvents);
        RequireArray(root, "remoteEndpoints", policy.MaxEvents);
        var nodes = RequireArray(root, "nodes", policy.MaxNodes);
        foreach (var node in nodes.EnumerateArray())
        {
            RequireArray(node, "children", policy.MaxChildren);
            foreach (var property in RequireArray(node, "properties", Enum.GetValues<UiPropertyId>().Length).EnumerateArray())
            {
                if (property.ValueKind == JsonValueKind.Object && property.TryGetProperty("literal", out var literal) && literal.ValueKind == JsonValueKind.Object)
                { CheckValue(literal, policy); }
            }
        }
    }

    private static void CheckOptionalCollections(JsonElement root, UiPolicy policy)
    {
        if (root.TryGetProperty("extensions", out _))
        { RequireArray(root, "extensions", policy.MaxNodes); }
        if (root.TryGetProperty("resources", out _))
        { RequireArray(root, "resources", policy.MaxResources); }
    }

    private static void CheckFeatures(JsonElement root)
    {
        foreach (var name in new[] { "requiredFeatures", "optionalFeatures" })
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out _))
            { RequireArray(root, name, Enum.GetValues<UiFeature>().Length); }
        }
    }

    private static void CheckValue(JsonElement value, UiPolicy policy)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("items", out var items) &&
            (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > policy.MaxItems))
        { throw new UiValidationException("UI list exceeds the host collection limit."); }
    }

    private static JsonElement RequireArray(JsonElement element, string name, int maximum)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maximum)
        {
            throw new UiValidationException("Missing UI array or host collection limit exceeded.");
        }

        return array;
    }
}
