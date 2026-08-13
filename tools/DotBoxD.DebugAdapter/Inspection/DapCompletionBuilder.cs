namespace DotBoxD.DebugAdapter;

internal static class DapCompletionBuilder
{
    public static object[] Build(
        IReadOnlyList<DapSourceVariableBinding> bindings,
        string text,
        int column)
    {
        var prefix = DapInspectionJson.CompletionPrefix(text, column);
        var separator = prefix.LastIndexOf('.');
        var parent = separator < 0 ? string.Empty : prefix[..(separator + 1)];
        var typed = separator < 0 ? prefix : prefix[(separator + 1)..];
        return bindings
            .Select(binding => binding.SourceName)
            .Where(path => path.StartsWith(parent, StringComparison.Ordinal))
            .Select(path => path[parent.Length..])
            .Select(path => path.Split('.')[0])
            .Where(path => path.StartsWith(typed, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (object)new { label = path, type = parent.Length == 0 ? "variable" : "property" })
            .ToArray();
    }
}
