namespace DotBoxD.Plugins.Runtime.Validation;

internal static class PluginRequiredCapabilityMetadata
{
    public static string[] Read(SandboxModule module)
    {
        if (!module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.RequiredCapabilities, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
