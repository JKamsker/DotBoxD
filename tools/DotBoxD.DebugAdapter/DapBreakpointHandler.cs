using System.Text.Json;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.DebugAdapter;

internal sealed class DapBreakpointHandler(
    DapConnection connection,
    BridgeClient bridge,
    string pluginId)
{
    private readonly Dictionary<string, BreakpointBinding[]> _sourceBindings =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _configuredPluginIds = new(StringComparer.Ordinal);

    public async ValueTask HandleAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = request.GetProperty("arguments");
        var path = RequiredString(arguments.GetProperty("source"), "path");
        var requested = arguments.TryGetProperty("breakpoints", out var breakpointValues)
            ? breakpointValues.EnumerateArray().Select(item => item.Clone()).ToArray()
            : [];
        var lines = requested.Select(item => item.GetProperty("line").GetInt32()).ToArray();
        var resolvedResponse = await bridge.SendAsync(
                "resolve",
                new Dictionary<string, object?>
                {
                    ["pluginId"] = pluginId,
                    ["path"] = path,
                    ["lines"] = lines
                },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureBridgeSuccess(resolvedResponse);
        var resolved = Property(resolvedResponse.GetProperty("body"), "Breakpoints", "breakpoints")
            .EnumerateArray().Select(item => item.Clone()).ToArray();
        _sourceBindings[path] = BuildBindings(requested, resolved);
        await SynchronizeRemoteBreakpointsAsync(cancellationToken).ConfigureAwait(false);
        await connection.RespondAsync(
                request,
                true,
                new { breakpoints = resolved.Select(DapBreakpoint).ToArray() },
                null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask SynchronizeRemoteBreakpointsAsync(CancellationToken cancellationToken)
    {
        var groups = _sourceBindings.Values
            .SelectMany(item => item)
            .GroupBy(item => item.PluginId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Specification).ToArray());
        var pluginIds = groups.Keys.Concat(_configuredPluginIds).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var id in pluginIds)
        {
            groups.TryGetValue(id, out var breakpoints);
            _ = await bridge.RemoteAsync(
                    PluginDebugCommands.SetBreakpoints,
                    new { pluginId = id, breakpoints = breakpoints ?? [] },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        _configuredPluginIds.Clear();
        _configuredPluginIds.UnionWith(groups.Keys);
    }

    private static BreakpointBinding[] BuildBindings(JsonElement[] requested, JsonElement[] resolved)
    {
        var bindings = new List<BreakpointBinding>();
        for (var index = 0; index < Math.Min(resolved.Length, requested.Length); index++)
        {
            var mapped = resolved[index];
            if (!Property(mapped, "Verified", "verified").GetBoolean())
            {
                continue;
            }

            var source = requested[index];
            foreach (var binding in Property(mapped, "Bindings", "bindings").EnumerateArray())
            {
                var pluginId = Property(binding, "PluginId", "pluginId").GetString();
                if (string.IsNullOrWhiteSpace(pluginId))
                {
                    throw new DebugAdapterException("invalidBinding", "A breakpoint binding is missing its plugin ID.");
                }

                bindings.Add(new BreakpointBinding(
                    pluginId,
                    new
                    {
                        nodeId = Property(binding, "NodeId", "nodeId").GetString(),
                        condition = OptionalString(source, "condition"),
                        hitCount = ParseHitCount(OptionalString(source, "hitCondition")),
                        logMessage = OptionalString(source, "logMessage")
                    }));
            }
        }

        return bindings.ToArray();
    }

    private static object DapBreakpoint(JsonElement resolved, int index)
    {
        var verified = Property(resolved, "Verified", "verified").GetBoolean();
        return new
        {
            id = index + 1,
            verified,
            message = OptionalPropertyString(resolved, "Message", "message"),
            line = Property(resolved, "Line", "line").GetInt32(),
            column = Property(resolved, "Column", "column").GetInt32()
        };
    }

    private static string RequiredString(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? throw new DebugAdapterException("invalidArguments", $"{name} is null.")
            : throw new DebugAdapterException("invalidArguments", $"{name} is required.");

    private static string? OptionalString(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? ParseHitCount(string? value)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private static JsonElement Property(JsonElement value, string first, string second)
        => value.TryGetProperty(first, out var property) ? property : value.GetProperty(second);

    private static string? OptionalPropertyString(JsonElement value, string first, string second)
    {
        var property = Property(value, first, second);
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static void EnsureBridgeSuccess(JsonElement response)
    {
        if (!response.GetProperty("success").GetBoolean())
        {
            throw new DebugAdapterException("bridgeError", response.GetProperty("error").GetString()!);
        }
    }

    private sealed record BreakpointBinding(string PluginId, object Specification);
}
