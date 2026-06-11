namespace SafeIR.Plugins;

using SafeIR;

public interface ILiveSetting
{
    string Name { get; }
    LiveSettingDefinition Definition { get; }
    object? CurrentValue { get; }
    SandboxValue ToSandboxValue();
    void SetObject(object? value);
}

public sealed class LiveValue<T> : ILiveSetting
{
    private readonly object _gate = new();
    private T _value;

    public LiveValue(string name, T value)
        : this(new LiveSettingDefinition(name, LiveSettingTypeConverter.FromClrType(typeof(T)), value), value)
    {
    }

    internal LiveValue(LiveSettingDefinition definition, T value)
    {
        Definition = definition;
        _value = value;
    }

    public string Name => Definition.Name;
    public LiveSettingDefinition Definition { get; }

    public T Value
    {
        get {
            lock (_gate) {
                return _value;
            }
        }
        set {
            lock (_gate) {
                _value = value;
            }
        }
    }

    public object? CurrentValue => Value;

    public SandboxValue ToSandboxValue()
        => LiveSettingTypeConverter.ToSandboxValue(Definition.Type, Value);

    public void SetObject(object? value)
        => Value = (T)LiveSettingTypeConverter.CoerceClr(typeof(T), value)!;
}

public sealed class LiveSettingStore
{
    private readonly Dictionary<string, ILiveSetting> _settings;

    public LiveSettingStore(IEnumerable<ILiveSetting> settings)
    {
        _settings = settings.ToDictionary(s => s.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<LiveSettingDefinition> Definitions
        => _settings.Values.Select(s => s.Definition).OrderBy(s => s.Name, StringComparer.Ordinal).ToArray();

    public T Get<T>(string name)
        => _settings.TryGetValue(name, out var setting)
            ? (T)LiveSettingTypeConverter.CoerceClr(typeof(T), setting.CurrentValue)!
            : throw new KeyNotFoundException($"Live setting '{name}' is not registered.");

    public object? GetObject(string name)
        => _settings.TryGetValue(name, out var setting)
            ? setting.CurrentValue
            : throw new KeyNotFoundException($"Live setting '{name}' is not registered.");

    public void Set<T>(string name, T value)
    {
        if (!_settings.TryGetValue(name, out var setting)) {
            throw new KeyNotFoundException($"Live setting '{name}' is not registered.");
        }

        setting.SetObject(value);
    }

    public void SetObject(string name, object? value)
    {
        if (!_settings.TryGetValue(name, out var setting)) {
            throw new KeyNotFoundException($"Live setting '{name}' is not registered.");
        }

        setting.SetObject(value);
    }

    public T As<T>() where T : class => LiveContextProxy<T>.Create(this);

    internal IReadOnlyList<SandboxValue> ToSandboxValues(IReadOnlyList<LiveSettingDefinition> orderedSettings)
        => orderedSettings.Select(s => _settings[s.Name].ToSandboxValue()).ToArray();

    internal static LiveSettingStore FromDefinitions(IEnumerable<LiveSettingDefinition> definitions)
    {
        var settings = definitions.Select(definition => {
            var value = definition.Type switch {
                "bool" => LiveSettingTypeConverter.CoerceClr(typeof(bool), definition.DefaultValue),
                "int" => LiveSettingTypeConverter.CoerceClr(typeof(int), definition.DefaultValue),
                "long" => LiveSettingTypeConverter.CoerceClr(typeof(long), definition.DefaultValue),
                "double" => LiveSettingTypeConverter.CoerceClr(typeof(double), definition.DefaultValue),
                "string" => LiveSettingTypeConverter.CoerceClr(typeof(string), definition.DefaultValue),
                _ => throw LiveSettingTypeConverter.Diagnostic($"Live setting type '{definition.Type}' is not supported.")
            };
            LiveSettingTypeConverter.ValidateRangeValue(definition, value);
            return new LiveSettingSlot(definition, value);
        });
        return new LiveSettingStore(settings);
    }

    private sealed class LiveSettingSlot(LiveSettingDefinition definition, object? value) : ILiveSetting
    {
        private readonly object _gate = new();
        private object? _value = value;

        public string Name => definition.Name;
        public LiveSettingDefinition Definition => definition;

        public object? CurrentValue
        {
            get {
                lock (_gate) {
                    return _value;
                }
            }
        }

        public SandboxValue ToSandboxValue()
            => LiveSettingTypeConverter.ToSandboxValue(definition.Type, CurrentValue);

        public void SetObject(object? value)
        {
            var coerced = definition.Type switch {
                "bool" => LiveSettingTypeConverter.CoerceClr(typeof(bool), value),
                "int" => LiveSettingTypeConverter.CoerceClr(typeof(int), value),
                "long" => LiveSettingTypeConverter.CoerceClr(typeof(long), value),
                "double" => LiveSettingTypeConverter.CoerceClr(typeof(double), value),
                "string" => LiveSettingTypeConverter.CoerceClr(typeof(string), value),
                _ => throw LiveSettingTypeConverter.Diagnostic($"Live setting type '{definition.Type}' is not supported.")
            };
            LiveSettingTypeConverter.ValidateRangeValue(definition, coerced);
            lock (_gate) {
                _value = coerced;
            }
        }
    }
}
