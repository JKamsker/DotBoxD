using System.Text.Json;

namespace DotBoxD.DebugAdapter;

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
