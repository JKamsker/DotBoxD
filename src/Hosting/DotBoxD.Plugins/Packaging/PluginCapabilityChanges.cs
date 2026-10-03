namespace DotBoxD.Plugins.Packaging;

/// <summary>Reviewable capability changes. Approval still belongs to host policy.</summary>
public sealed class PluginCapabilityChanges
{
    private PluginCapabilityChanges(string[] added, string[] removed, string[] unchanged)
    {
        Added = Array.AsReadOnly(added);
        Removed = Array.AsReadOnly(removed);
        Unchanged = Array.AsReadOnly(unchanged);
    }

    public IReadOnlyList<string> Added { get; }
    public IReadOnlyList<string> Removed { get; }
    public IReadOnlyList<string> Unchanged { get; }
    public bool RequiresAdditionalPermission => Added.Count != 0;

    public static PluginCapabilityChanges Compare(PluginManifest previous, PluginManifest next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        if (!string.Equals(previous.PluginId, next.PluginId, StringComparison.Ordinal))
        {
            throw new ArgumentException("An upgrade must preserve the plugin ID.", nameof(next));
        }
        return new PluginCapabilityChanges(
            next.RequiredCapabilities.Except(previous.RequiredCapabilities, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            previous.RequiredCapabilities.Except(next.RequiredCapabilities, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            next.RequiredCapabilities.Intersect(previous.RequiredCapabilities, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
