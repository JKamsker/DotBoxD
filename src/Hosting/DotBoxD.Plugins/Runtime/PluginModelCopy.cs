using System.Collections.ObjectModel;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginModelCopy
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}
