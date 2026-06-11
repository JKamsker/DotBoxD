namespace SafeIR.Plugins;

using System.Reflection;

internal static class LiveKernelValueFactory
{
    public static T Create<T>(InstalledKernel kernel) where T : class
    {
        if (typeof(T).IsInterface) {
            return kernel.Value.As<T>();
        }

        var state = Activator.CreateInstance<T>();
        var properties = LiveProperties(typeof(T));
        PullFromStore(kernel, state, properties);
        kernel.RegisterStateSynchronizer(() => PushToStore(kernel, state, properties));
        return state;
    }

    private static IReadOnlyList<PropertyInfo> LiveProperties(Type type)
    {
        var marked = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite && Attribute.IsDefined(p, typeof(LiveSettingAttribute)))
            .ToArray();
        if (marked.Length > 0) {
            return marked;
        }

        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();
    }

    private static void PullFromStore<T>(InstalledKernel kernel, T state, IReadOnlyList<PropertyInfo> properties)
    {
        foreach (var property in properties) {
            if (!kernel.Manifest.LiveSettings.Any(s => string.Equals(s.Name, property.Name, StringComparison.Ordinal))) {
                continue;
            }

            var value = LiveSettingTypeConverter.CoerceClr(property.PropertyType, kernel.Value.GetObject(property.Name));
            property.SetValue(state, value);
        }
    }

    private static void PushToStore<T>(InstalledKernel kernel, T state, IReadOnlyList<PropertyInfo> properties)
    {
        foreach (var property in properties) {
            if (!kernel.Manifest.LiveSettings.Any(s => string.Equals(s.Name, property.Name, StringComparison.Ordinal))) {
                continue;
            }

            kernel.Value.SetObject(property.Name, property.GetValue(state));
        }
    }
}
