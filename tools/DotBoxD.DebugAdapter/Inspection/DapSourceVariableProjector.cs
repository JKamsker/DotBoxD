using System.Text.Json;

namespace DotBoxD.DebugAdapter;

internal static class DapSourceVariableProjector
{
    public static JsonElement Map(
        JsonElement variables,
        IReadOnlyList<DapSourceVariableBinding> bindings,
        bool includeSynthetic)
    {
        if (bindings.Count == 0)
        {
            return variables.Clone();
        }

        var raw = variables.EnumerateArray()
            .ToDictionary(variable => variable.GetProperty("name").GetString()!, StringComparer.Ordinal);
        var roots = new Dictionary<string, SourceNode>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (!raw.ContainsKey(binding.SlotName) && (!includeSynthetic || binding.DisplayValue is null))
            {
                continue;
            }

            var segments = binding.SourceName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var node = GetOrAdd(roots, segments[0]);
            foreach (var segment in segments.Skip(1))
            {
                node = GetOrAdd(node.Children, segment);
            }

            node.Binding = binding;
        }

        return JsonSerializer.SerializeToElement(roots.Values.Select(node => Snapshot(node, raw)));
    }

    public static bool TryEvaluate(
        JsonElement arguments,
        JsonElement locals,
        IReadOnlyList<DapSourceVariableBinding> bindings,
        string expression,
        out JsonElement value)
    {
        var variables = Map(arguments, bindings, includeSynthetic: true).EnumerateArray()
            .Concat(Map(locals, bindings, includeSynthetic: false).EnumerateArray());
        var segments = expression.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = variables.FirstOrDefault(variable =>
            segments.Length > 0 && variable.GetProperty("name").GetString() == segments[0]);
        if (current.ValueKind == JsonValueKind.Undefined || !current.TryGetProperty("value", out value))
        {
            value = default;
            return false;
        }

        for (var index = 1; index < segments.Length; index++)
        {
            if (!value.TryGetProperty("children", out var children))
            {
                value = default;
                return false;
            }

            var child = children.EnumerateArray().FirstOrDefault(candidate =>
                candidate.GetProperty("name").GetString() == segments[index]);
            if (child.ValueKind == JsonValueKind.Undefined || !child.TryGetProperty("value", out value))
            {
                value = default;
                return false;
            }
        }

        value = value.Clone();
        return true;
    }

    public static string Translate(string expression, IReadOnlyList<DapSourceVariableBinding> bindings)
        => DapSourceExpressionTranslator.Translate(expression, bindings);

    private static object Snapshot(SourceNode node, IReadOnlyDictionary<string, JsonElement> raw)
    {
        raw.TryGetValue(node.Binding?.SlotName ?? string.Empty, out var variable);
        var children = node.Children.Values.Select(child => new
        {
            name = child.Name,
            value = Value(child, raw)
        }).ToArray();
        var hasRawValue = variable.ValueKind == JsonValueKind.Object &&
            variable.GetProperty("assigned").GetBoolean() &&
            variable.GetProperty("value").ValueKind == JsonValueKind.Object;
        JsonElement? value = children.Length > 0
            ? JsonSerializer.SerializeToElement(new
            {
                type = node.Binding?.TypeName ?? "object",
                value = node.Binding?.DisplayValue,
                children
            })
            : hasRawValue
                ? variable.GetProperty("value").Clone()
                : node.Binding?.DisplayValue is { } display
                    ? JsonSerializer.SerializeToElement(new { type = node.Binding.TypeName ?? "object", value = display })
                    : null;
        return new
        {
            name = node.Name,
            kind = variable.ValueKind == JsonValueKind.Object
                ? variable.GetProperty("kind").GetString()
                : "Argument",
            type = node.Binding?.TypeName ??
                (variable.ValueKind == JsonValueKind.Object ? variable.GetProperty("type").GetString() : "object"),
            assigned = value is not null,
            value
        };
    }

    private static JsonElement Value(SourceNode node, IReadOnlyDictionary<string, JsonElement> raw)
    {
        var snapshot = JsonSerializer.SerializeToElement(Snapshot(node, raw));
        var value = snapshot.GetProperty("value");
        return value.ValueKind == JsonValueKind.Object
            ? value.Clone()
            : JsonSerializer.SerializeToElement(new
            {
                type = node.Binding?.TypeName ?? "object",
                value = (string?)null
            });
    }

    private static SourceNode GetOrAdd(IDictionary<string, SourceNode> nodes, string name)
    {
        if (!nodes.TryGetValue(name, out var node))
        {
            node = new SourceNode(name);
            nodes.Add(name, node);
        }

        return node;
    }

    private sealed class SourceNode(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, SourceNode> Children { get; } = new(StringComparer.Ordinal);
        public DapSourceVariableBinding? Binding { get; set; }
    }
}

internal sealed record DapSourceVariableBinding(
    string SlotName,
    string SourceName,
    string? TypeName,
    string? DisplayValue);
