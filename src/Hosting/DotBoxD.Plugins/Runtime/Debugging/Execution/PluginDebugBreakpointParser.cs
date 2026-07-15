using System.Text.Json;
using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugBreakpointParser(int maxExpressionLength)
{
    public IReadOnlyList<PluginDebugBreakpointSpec> Parse(JsonElement payload)
    {
        if (payload.TryGetProperty("nodeIds", out var nodeIds) && nodeIds.ValueKind == JsonValueKind.Array)
        {
            return nodeIds.EnumerateArray()
                .Select(value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                    ? new PluginDebugBreakpointSpec(new SandboxNodeId(value.GetString()!))
                    : throw new ArgumentException("Each nodeIds entry must be a non-empty string."))
                .DistinctBy(breakpoint => breakpoint.NodeId)
                .ToArray();
        }

        if (!payload.TryGetProperty("breakpoints", out var breakpoints) ||
            breakpoints.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("setBreakpoints requires nodeIds or breakpoints.");
        }

        var parsed = breakpoints.EnumerateArray().Select(ParseBreakpoint);
        return parsed.DistinctBy(breakpoint => breakpoint.NodeId).ToArray();
    }

    private PluginDebugBreakpointSpec ParseBreakpoint(JsonElement breakpoint)
    {
        if (!TryReadString(breakpoint, "nodeId", out var nodeId))
        {
            throw new ArgumentException("Each breakpoint requires a structural nodeId.");
        }

        var condition = OptionalString(breakpoint, "condition");
        var logMessage = OptionalString(breakpoint, "logMessage");
        EnsureExpressionLimit(condition, "condition");
        EnsureExpressionLimit(logMessage, "logMessage");
        var hitCount = breakpoint.TryGetProperty("hitCount", out var hitValue)
            ? (int?)PositiveHitCount(hitValue)
            : null;
        return new PluginDebugBreakpointSpec(new SandboxNodeId(nodeId!), condition, hitCount, logMessage);
    }

    private static int PositiveHitCount(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count <= 0)
        {
            throw new ArgumentException("Breakpoint hitCount must be a positive integer.");
        }

        return count;
    }

    private void EnsureExpressionLimit(string? value, string name)
    {
        if (value?.Length > maxExpressionLength)
        {
            throw new ArgumentException(
                $"Breakpoint {name} exceeds the {maxExpressionLength}-character host limit.");
        }
    }

    private static string? OptionalString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"Breakpoint {name} must be a string.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException($"Breakpoint {name} cannot be empty.");
        }

        return text;
    }

    private static bool TryReadString(JsonElement payload, string name, out string? value)
    {
        value = null;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }
}
