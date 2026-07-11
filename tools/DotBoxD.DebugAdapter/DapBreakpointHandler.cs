using System.Text.Json;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.DebugAdapter;

internal sealed class DapBreakpointHandler(
    DapConnection connection,
    BridgeClient bridge,
    string pluginId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, JsonElement[]> _sourceRequests =
        new(StringComparer.OrdinalIgnoreCase);
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
        JsonElement[] resolved;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _sourceRequests[path] = requested;
            resolved = await ResolveAsync(path, requested, cancellationToken).ConfigureAwait(false);
            await SynchronizeRemoteBreakpointsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await connection.RespondAsync(
                request,
                true,
                new { breakpoints = resolved.Select(DapBreakpoint).ToArray() },
                null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask OnSourcesChangedAsync()
    {
        var changed = new List<JsonElement>();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var (path, requested) in _sourceRequests)
            {
                changed.AddRange(await ResolveAsync(path, requested, CancellationToken.None).ConfigureAwait(false));
            }

            await SynchronizeRemoteBreakpointsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        for (var index = 0; index < changed.Count; index++)
        {
            await connection.EventAsync(
                    "breakpoint",
                    new { reason = "changed", breakpoint = DapBreakpoint(changed[index], index) })
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<JsonElement[]> ResolveAsync(
        string path,
        JsonElement[] requested,
        CancellationToken cancellationToken)
    {
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
        return resolved;
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
                    BreakpointSpecification(source, binding)));
            }
        }

        return bindings.ToArray();
    }

    private static IReadOnlyDictionary<string, object> BreakpointSpecification(
        JsonElement source,
        JsonElement binding)
    {
        var specification = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["nodeId"] = Property(binding, "NodeId", "nodeId").GetString()
                ?? throw new DebugAdapterException("invalidBinding", "A breakpoint binding is missing its node ID.")
        };
        AddOptional(specification, "condition", OptionalString(source, "condition"));
        AddOptional(specification, "hitCount", ParseHitCount(OptionalString(source, "hitCondition")));
        AddOptional(specification, "logMessage", OptionalString(source, "logMessage"));
        return specification;
    }

    private static void AddOptional(Dictionary<string, object> target, string name, object? value)
    {
        if (value is not null)
        {
            target[name] = value;
        }
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
