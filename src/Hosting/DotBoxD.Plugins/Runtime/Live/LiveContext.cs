using System.Reflection;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Plugins.Runtime;

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
    private static readonly IReadOnlyDictionary<MethodInfo, LiveContextAccessor> Accessors = CreateAccessors();
    private LiveSettingStore? _settings;
    private Func<bool>? _isRevoked;

    public static T Create(LiveSettingStore settings, Func<bool>? isRevoked = null)
    {
        RejectIndexers();
        var proxy = Create<T, LiveContextProxy<T>>();
        var context = (LiveContextProxy<T>)(object)proxy;
        context._settings = settings;
        context._isRevoked = isRevoked;
        return proxy;
    }

    private static void RejectIndexers()
    {
        if (LiveContextPropertyDiscovery.GetProperties(typeof(T))
            .Any(static property => property.GetIndexParameters().Length > 0))
        {
            throw LiveSettingTypeConverter.Diagnostic(
                $"Live context binding '{typeof(T).Name}' cannot declare indexer properties.");
        }
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        return Accessors.TryGetValue(targetMethod, out var accessor)
            ? Invoke(accessor, args)
            : throw new NotSupportedException($"Live context method '{targetMethod.Name}' is not supported.");
    }

    private object? Invoke(LiveContextAccessor accessor, object?[]? args)
    {
        var settings = _settings ?? throw new InvalidOperationException("Live context proxy is not initialized.");
        PluginKernelRevocation.ThrowIfRevoked(_isRevoked?.Invoke() == true);
        if (accessor.IsGetter)
        {
            return LiveSettingTypeConverter.CoerceClr(
                accessor.PropertyType,
                settings.GetObject(accessor.PropertyName));
        }

        settings.SetObject(accessor.PropertyName, args is { Length: 1 } ? args[0] : null);
        return null;
    }

    private static IReadOnlyDictionary<MethodInfo, LiveContextAccessor> CreateAccessors()
    {
        var accessors = new Dictionary<MethodInfo, LiveContextAccessor>();
        foreach (var property in LiveContextPropertyDiscovery.GetProperties(typeof(T)))
        {
            if (property.GetMethod is { } getter)
            {
                accessors.Add(getter, new LiveContextAccessor(property.Name, property.PropertyType, IsGetter: true));
            }

            if (property.SetMethod is { } setter)
            {
                accessors.Add(setter, new LiveContextAccessor(property.Name, property.PropertyType, IsGetter: false));
            }
        }

        return accessors;
    }
}

internal readonly record struct LiveContextAccessor(string PropertyName, Type PropertyType, bool IsGetter);

internal static class LiveContextFactory
{
    public static LiveContext<T> Create<T>(string name, Action<T>? initialize = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!typeof(T).IsInterface)
        {
            throw LiveSettingTypeConverter.Diagnostic("Live context bindings must use an interface type.");
        }

        var definitions = new Dictionary<string, LiveSettingDefinition>(StringComparer.Ordinal);
        foreach (var property in LiveContextPropertyDiscovery.GetProperties(typeof(T)))
        {
            var definition = CreateDefinition(property);
            if (definitions.TryGetValue(definition.Name, out var existing) && existing.Type != definition.Type)
            {
                throw LiveSettingTypeConverter.Diagnostic(
                    $"Live setting '{definition.Name}' has conflicting inherited property types.");
            }

            definitions.TryAdd(definition.Name, definition);
        }

        var settings = LiveSettingStore.FromDefinitions(definitions.Values);
        var context = new LiveContext<T>(name, settings);
        initialize?.Invoke(context.Value);
        return context;
    }

    private static LiveSettingDefinition CreateDefinition(PropertyInfo property)
    {
        if (!property.CanRead || !property.CanWrite)
        {
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
