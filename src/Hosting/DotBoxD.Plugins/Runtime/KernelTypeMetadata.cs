namespace DotBoxD.Plugins.Runtime;

internal static class KernelTypeMetadata
{
    public static string PluginId(Type kernelType, Func<Type, PluginPackage> resolvePackage)
    {
        ArgumentNullException.ThrowIfNull(resolvePackage);

        var attribute = Attribute.GetCustomAttribute(kernelType, typeof(PluginAttribute)) as PluginAttribute;
        if (attribute?.Id is null)
        {
            return resolvePackage(kernelType).Manifest.PluginId;
        }

        if (string.IsNullOrWhiteSpace(attribute.Id))
        {
            throw new InvalidOperationException(
                $"Kernel type '{kernelType.FullName}' must declare a non-empty PluginAttribute id.");
        }

        return attribute.Id;
    }
}
