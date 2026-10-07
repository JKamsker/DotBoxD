using System.Text.Json;

namespace DotBoxD.UI;

internal static class UiJsonBudget
{
    public static void Validate(JsonElement root, UiPolicy policy)
    {
        RequireArray(root, "state", policy.MaxStateSlots);
        RequireArray(root, "kernels", policy.MaxKernels);
        RequireArray(root, "events", policy.MaxEvents);
        RequireArray(root, "remoteEndpoints", policy.MaxEvents);
        var nodes = RequireArray(root, "nodes", policy.MaxNodes);
        foreach (var node in nodes.EnumerateArray())
        {
            RequireArray(node, "children", policy.MaxChildren);
            RequireArray(node, "properties", 6);
        }
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
