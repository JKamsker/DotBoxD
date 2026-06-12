namespace SafeIR.Plugins;

using System.Reflection;

public sealed class LiveContext<T> where T : class
{
    internal LiveContext(string name, LiveSettingStore settings)
    {
        Name = name;
        Settings = settings;
        Value = settings.As<T>();
    }

    public string Name { get; }
    public T Value { get; }
    public LiveSettingStore Settings { get; }
}

internal class LiveContextProxy<T> : DispatchProxy where T : class
{
    private LiveSettingStore? _settings;

    public static T Create(LiveSettingStore settings)
    {
        var proxy = Create<T, LiveContextProxy<T>>();
        ((LiveContextProxy<T>)(object)proxy)._settings = settings;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var propertyName = PropertyName(targetMethod);
        if (targetMethod.Name.StartsWith("get_", StringComparison.Ordinal)) {
            return Read(propertyName, targetMethod.ReturnType);
        }

        if (targetMethod.Name.StartsWith("set_", StringComparison.Ordinal)) {
            Write(propertyName, args is { Length: 1 } ? args[0] : null);
            return null;
        }

        throw new NotSupportedException($"Live context method '{targetMethod.Name}' is not supported.");
    }

    private object? Read(string propertyName, Type propertyType)
    {
        var settings = _settings ?? throw new InvalidOperationException("Live context proxy is not initialized.");
        return typeof(LiveSettingStore)
            .GetMethod(nameof(LiveSettingStore.Get))!
            .MakeGenericMethod(propertyType)
            .Invoke(settings, [propertyName]);
    }

    private void Write(string propertyName, object? value)
    {
        var settings = _settings ?? throw new InvalidOperationException("Live context proxy is not initialized.");
        var currentType = value?.GetType() ?? typeof(string);
        typeof(LiveSettingStore)
            .GetMethod(nameof(LiveSettingStore.Set))!
            .MakeGenericMethod(currentType)
            .Invoke(settings, [propertyName, value]);
    }

    private static string PropertyName(MethodInfo method)
        => method.Name.Length > 4 ? method.Name[4..] : method.Name;
}

internal static class LiveContextFactory
{
    public static LiveContext<T> Create<T>(string name, Action<T>? initialize = null) where T : class
    {
        if (!typeof(T).IsInterface) {
            throw LiveSettingTypeConverter.Diagnostic("Live context bindings must use an interface type.");
        }

        var definitions = typeof(T).GetProperties()
            .Select(CreateDefinition)
            .ToArray();
        var settings = LiveSettingStore.FromDefinitions(definitions);
        var context = new LiveContext<T>(name, settings);
        initialize?.Invoke(context.Value);
        return context;
    }

    private static LiveSettingDefinition CreateDefinition(PropertyInfo property)
    {
        if (!property.CanRead || !property.CanWrite) {
            throw LiveSettingTypeConverter.Diagnostic(
                $"Live setting '{property.Name}' must expose both get and set accessors.");
        }

        var type = LiveSettingTypeConverter.FromClrType(property.PropertyType);
        return new LiveSettingDefinition(
            property.Name,
            type,
            LiveSettingTypeConverter.DefaultFor(property.PropertyType));
    }
}
