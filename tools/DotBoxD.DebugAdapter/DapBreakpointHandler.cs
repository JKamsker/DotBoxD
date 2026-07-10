using System.Text.Json;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.DebugAdapter;

internal sealed class DapBreakpointHandler(
    DapConnection connection,
    BridgeClient bridge,
    string pluginId)
{
    public async ValueTask HandleAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = request.GetProperty("arguments");
        var path = RequiredString(arguments.GetProperty("source"), "path");
        var requested = arguments.TryGetProperty("breakpoints", out var breakpointValues)
            ? breakpointValues.EnumerateArray().Select(item => item.Clone()).ToArray()
            : [];
        var lines = requested.Select(item => item.GetProperty("line").GetInt32() - 1).ToArray();
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
        _ = await bridge.RemoteAsync(
                PluginDebugCommands.SetBreakpoints,
                new { pluginId, breakpoints = BuildRemoteBreakpoints(requested, resolved) },
                cancellationToken)
            .ConfigureAwait(false);
        await connection.RespondAsync(
                request,
                true,
                new { breakpoints = resolved.Select(DapBreakpoint).ToArray() },
                null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static object[] BuildRemoteBreakpoints(JsonElement[] requested, JsonElement[] resolved)
    {
        var breakpoints = new List<object>();
        for (var index = 0; index < resolved.Length; index++)
        {
            var mapped = resolved[index];
            if (!Property(mapped, "Verified", "verified").GetBoolean())
            {
                continue;
            }

            var source = requested[index];
            breakpoints.Add(new
            {
                nodeId = Property(mapped, "NodeId", "nodeId").GetString(),
                condition = OptionalString(source, "condition"),
                hitCount = ParseHitCount(OptionalString(source, "hitCondition")),
                logMessage = OptionalString(source, "logMessage")
            });
        }

        return breakpoints.ToArray();
    }

    private static object DapBreakpoint(JsonElement resolved, int index)
    {
        var verified = Property(resolved, "Verified", "verified").GetBoolean();
        return new
        {
            id = index + 1,
            verified,
            message = OptionalPropertyString(resolved, "Message", "message"),
            line = Property(resolved, "Line", "line").GetInt32() + 1,
            column = Property(resolved, "Column", "column").GetInt32() + 1
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
}

internal sealed class DapVariableStore
{
    private readonly Dictionary<int, DapVariableHandle> _handles = [];
    private int _nextReference;

    public object Scopes(string frameId)
    {
        var arguments = Add(new DapVariableHandle(frameId, "arguments", default));
        var locals = Add(new DapVariableHandle(frameId, "locals", default));
        return new
        {
            scopes = new[]
            {
                new { name = "Arguments", variablesReference = arguments, expensive = false },
                new { name = "Locals", variablesReference = locals, expensive = false }
            }
        };
    }

    public bool TryGet(int reference, out DapVariableHandle handle) =>
        _handles.TryGetValue(reference, out handle!);

    public DapVariableHandle Get(int reference) =>
        TryGet(reference, out var handle)
            ? handle
            : throw new DebugAdapterException("staleVariables", "The variable reference is no longer available.");

    public object[] ScopeVariables(JsonElement variables) =>
        variables.EnumerateArray().Select(Variable).ToArray();

    public object[] Expand(JsonElement value)
    {
        if (value.TryGetProperty("children", out var children))
        {
            return children.EnumerateArray().Select(child => Child(
                child.GetProperty("name").GetString()!, child.GetProperty("value"))).ToArray();
        }

        return value.TryGetProperty("entries", out var entries)
            ? entries.EnumerateArray().Select(entry => Child(
                "[" + Display(entry.GetProperty("key")) + "]", entry.GetProperty("value"))).ToArray()
            : [];
    }

    public int ValueReference(JsonElement value)
        => value.TryGetProperty("children", out _) || value.TryGetProperty("entries", out _)
            ? Add(new DapVariableHandle(string.Empty, string.Empty, value.Clone()))
            : 0;

    public static string Display(JsonElement value)
    {
        if (!value.TryGetProperty("value", out var scalar) || scalar.ValueKind == JsonValueKind.Null)
        {
            return value.GetProperty("type").GetString() ?? "unit";
        }

        return scalar.ValueKind == JsonValueKind.String ? scalar.GetString()! : scalar.ToString();
    }

    private object Variable(JsonElement variable)
    {
        var assigned = variable.GetProperty("assigned").GetBoolean();
        var value = variable.GetProperty("value");
        return new
        {
            name = variable.GetProperty("name").GetString(),
            value = assigned && value.ValueKind == JsonValueKind.Object ? Display(value) : "<unassigned>",
            type = variable.GetProperty("type").GetString(),
            variablesReference = assigned && value.ValueKind == JsonValueKind.Object ? ValueReference(value) : 0
        };
    }

    private object Child(string name, JsonElement value) => new
    {
        name,
        value = Display(value),
        type = value.GetProperty("type").GetString(),
        variablesReference = ValueReference(value)
    };

    private int Add(DapVariableHandle handle)
    {
        var reference = Interlocked.Increment(ref _nextReference);
        _handles[reference] = handle;
        return reference;
    }
}

internal sealed record DapVariableHandle(string FrameId, string Scope, JsonElement Value);
