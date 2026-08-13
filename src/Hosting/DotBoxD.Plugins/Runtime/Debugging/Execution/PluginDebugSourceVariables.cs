using System.Text.Json.Serialization;
using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Plugins.Debugging;

internal static class PluginDebugSourceVariables
{
    public static IReadOnlyList<PluginDebugSourceVariable> Map(
        IReadOnlyList<SandboxDebugVariable> variables,
        KernelDebugInfo? debugInfo,
        string functionId,
        SandboxDebugVariableKind scopeKind)
    {
        var bindings = Bindings(debugInfo, functionId);
        if (bindings.Length == 0)
        {
            return variables.Select(Raw).ToArray();
        }

        var raw = variables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var roots = new Dictionary<string, SourceNode>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (!raw.ContainsKey(binding.SlotName) &&
                (binding.DisplayValue is null || scopeKind != SandboxDebugVariableKind.Argument))
            {
                continue;
            }

            var segments = binding.SourceName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var node = GetOrAdd(roots, segments[0]);
            for (var index = 1; index < segments.Length; index++)
            {
                node = GetOrAdd(node.Children, segments[index]);
            }

            node.Binding = binding;
        }

        return roots.Values.Select(node => Snapshot(node, raw)).ToArray();
    }

    public static bool TryEvaluate(
        IReadOnlyList<SandboxDebugVariable> variables,
        KernelDebugInfo? debugInfo,
        string functionId,
        string expression,
        out PluginDebugValueSnapshot? value)
    {
        var segments = expression.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = Map(
                variables.Where(variable => variable.Kind == SandboxDebugVariableKind.Argument).ToArray(),
                debugInfo,
                functionId,
                SandboxDebugVariableKind.Argument)
            .Concat(Map(
                variables.Where(variable => variable.Kind == SandboxDebugVariableKind.Local).ToArray(),
                debugInfo,
                functionId,
                SandboxDebugVariableKind.Local))
            .FirstOrDefault(variable => segments.Length > 0 && variable.Name == segments[0])?.Value;
        for (var index = 1; current is not null && index < segments.Length; index++)
        {
            current = current.Children?.FirstOrDefault(child => child.Name == segments[index])?.Value;
        }

        value = current;
        return value is not null;
    }

    public static IReadOnlyList<string> CompletionPaths(KernelDebugInfo? debugInfo, string functionId)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in Bindings(debugInfo, functionId))
        {
            var path = binding.SourceName;
            while (path.Length > 0)
            {
                paths.Add(path);
                var separator = path.LastIndexOf('.');
                path = separator < 0 ? string.Empty : path[..separator];
            }
        }

        return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static KernelDebugVariableBinding[] Bindings(KernelDebugInfo? debugInfo, string functionId)
        => debugInfo?.VariableBindings
            .Where(binding => string.Equals(binding.FunctionId, functionId, StringComparison.Ordinal))
            .ToArray() ?? [];

    private static PluginDebugSourceVariable Snapshot(
        SourceNode node,
        IReadOnlyDictionary<string, SandboxDebugVariable> raw)
    {
        raw.TryGetValue(node.Binding?.SlotName ?? string.Empty, out var variable);
        var children = node.Children.Values.Select(child => ChildSnapshot(child, raw)).ToArray();
        PluginDebugValueSnapshot? value;
        bool assigned;
        if (children.Length > 0)
        {
            assigned = true;
            value = new PluginDebugValueSnapshot(
                node.Binding?.TypeName ?? "object",
                node.Binding?.DisplayValue,
                children);
        }
        else if (variable?.Value is not null)
        {
            assigned = true;
            value = PluginDebugValueCodec.Snapshot(variable.Value);
        }
        else if (node.Binding?.DisplayValue is { } displayValue)
        {
            assigned = true;
            value = new PluginDebugValueSnapshot(node.Binding.TypeName ?? "object", displayValue);
        }
        else
        {
            assigned = false;
            value = null;
        }

        return new PluginDebugSourceVariable(
            node.Name,
            (variable?.Kind ?? FirstKind(node, raw)).ToString(),
            node.Binding?.TypeName ?? value?.Type ?? variable?.Type.ToString() ?? "object",
            assigned,
            value);
    }

    private static PluginDebugChildValue ChildSnapshot(
        SourceNode node,
        IReadOnlyDictionary<string, SandboxDebugVariable> raw)
    {
        var source = Snapshot(node, raw);
        var value = source.Value ?? new PluginDebugValueSnapshot(source.Type, null);
        return new PluginDebugChildValue(source.Name, value);
    }

    private static SandboxDebugVariableKind FirstKind(
        SourceNode node,
        IReadOnlyDictionary<string, SandboxDebugVariable> raw)
    {
        foreach (var descendant in Descendants(node))
        {
            if (descendant.Binding is not null && raw.TryGetValue(descendant.Binding.SlotName, out var variable))
            {
                return variable.Kind;
            }
        }

        return SandboxDebugVariableKind.Local;
    }

    private static IEnumerable<SourceNode> Descendants(SourceNode node)
        => node.Children.Values.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static PluginDebugSourceVariable Raw(SandboxDebugVariable variable)
        => new(
            variable.Name,
            variable.Kind.ToString(),
            variable.Type.ToString(),
            variable.IsAssigned,
            variable.Value is null ? null : PluginDebugValueCodec.Snapshot(variable.Value));

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
        public KernelDebugVariableBinding? Binding { get; set; }
    }
}

internal sealed record PluginDebugSourceVariable(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("assigned")] bool Assigned,
    [property: JsonPropertyName("value")] PluginDebugValueSnapshot? Value);
