namespace DotBoxD.Plugins.Runtime;

internal static class PluginPackageRootValidator
{
    internal static void Validate(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(package.Manifest, nameof(PluginPackage.Manifest));
        ArgumentNullException.ThrowIfNull(package.Module, nameof(PluginPackage.Module));
        ArgumentNullException.ThrowIfNull(package.Entrypoints, nameof(PluginPackage.Entrypoints));
    }
}
