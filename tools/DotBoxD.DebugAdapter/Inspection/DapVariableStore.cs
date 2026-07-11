using System.Text.Json;

namespace DotBoxD.DebugAdapter;

internal sealed class DapVariableStore
{
    private readonly Dictionary<int, DapVariableHandle> _handles = [];
    private int _nextReference;

    public void Clear() => _handles.Clear();

    public void RemoveFrames(IReadOnlySet<string> frameIds)
    {
        var references = _handles
            .Where(item => frameIds.Contains(item.Value.FrameId))
            .Select(item => item.Key)
            .ToArray();
        foreach (var reference in references)
        {
            _handles.Remove(reference);
        }
    }

    public object Scopes(string frameId)
    {
        var arguments = Add(new DapVariableHandle(frameId, "arguments", default, null, []));
        var locals = Add(new DapVariableHandle(frameId, "locals", default, null, []));
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

    public IReadOnlyList<object> ChildPath(DapVariableHandle parent, string name)
    {
        if (parent.Value.TryGetProperty("children", out var children))
        {
            var values = children.EnumerateArray().ToArray();
            var index = Array.FindIndex(values, child => child.GetProperty("name").GetString() == name);
            if (index >= 0)
            {
                var kind = parent.Value.GetProperty("type").GetString()!.StartsWith("List", StringComparison.Ordinal)
                    ? "list"
                    : "record";
                return parent.Path.Append<object>(new { kind, index }).ToArray();
            }
        }
        else if (parent.Value.TryGetProperty("entries", out var entries))
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if ("[" + Display(entry.GetProperty("key")) + "]" == name)
                {
                    return parent.Path.Append<object>(new
                    {
                        kind = "map",
                        key = entry.GetProperty("key").Clone()
                    }).ToArray();
                }
            }
        }

        throw new DebugAdapterException("unknownVariable", $"The container has no child named '{name}'.");
    }

    public object[] ScopeVariables(JsonElement variables, DapVariableHandle scope) =>
        variables.EnumerateArray().Select(variable => Variable(variable, scope)).ToArray();

    public object[] Expand(DapVariableHandle handle)
    {
        var value = handle.Value;
        if (value.TryGetProperty("children", out var children))
        {
            var kind = value.GetProperty("type").GetString()!.StartsWith("List", StringComparison.Ordinal)
                ? "list"
                : "record";
            return children.EnumerateArray().Select((child, index) => Child(
                child.GetProperty("name").GetString()!,
                child.GetProperty("value"),
                handle,
                new { kind, index })).ToArray();
        }

        return value.TryGetProperty("entries", out var entries)
            ? entries.EnumerateArray().Select(entry => Child(
                "[" + Display(entry.GetProperty("key")) + "]",
                entry.GetProperty("value"),
                handle,
                new { kind = "map", key = entry.GetProperty("key").Clone() })).ToArray()
            : [];
    }

    public int ValueReference(
        JsonElement value,
        string frameId = "",
        string? variableName = null,
        IReadOnlyList<object>? path = null)
        => value.TryGetProperty("children", out _) || value.TryGetProperty("entries", out _)
            ? Add(new DapVariableHandle(frameId, string.Empty, value.Clone(), variableName, path ?? []))
            : 0;

    public static string Display(JsonElement value)
    {
        if (!value.TryGetProperty("value", out var scalar) || scalar.ValueKind == JsonValueKind.Null)
        {
            return value.GetProperty("type").GetString() ?? "unit";
        }

        return scalar.ValueKind == JsonValueKind.String ? scalar.GetString()! : scalar.ToString();
    }

    private object Variable(JsonElement variable, DapVariableHandle scope)
    {
        var assigned = variable.GetProperty("assigned").GetBoolean();
        var value = variable.GetProperty("value");
        var name = variable.GetProperty("name").GetString()!;
        return new
        {
            name,
            value = assigned && value.ValueKind == JsonValueKind.Object ? Display(value) : "<unassigned>",
            type = variable.GetProperty("type").GetString(),
            variablesReference = assigned && value.ValueKind == JsonValueKind.Object
                ? ValueReference(value, scope.FrameId, name)
                : 0
        };
    }

    private object Child(string name, JsonElement value, DapVariableHandle parent, object segment)
    {
        var path = parent.Path.Append(segment).ToArray();
        return new
        {
            name,
            value = Display(value),
            type = value.GetProperty("type").GetString(),
            variablesReference = ValueReference(value, parent.FrameId, parent.VariableName, path)
        };
    }

    private int Add(DapVariableHandle handle)
    {
        var reference = Interlocked.Increment(ref _nextReference);
        _handles[reference] = handle;
        return reference;
    }
}

internal sealed record DapVariableHandle(
    string FrameId,
    string Scope,
    JsonElement Value,
    string? VariableName,
    IReadOnlyList<object> Path);
