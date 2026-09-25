namespace DotBoxD.Plugins.Runtime;

internal sealed class LiveSettingUpdateTransaction(LiveSettingUpdateTransaction? parent = null)
{
    private readonly Dictionary<ILiveSetting, object?> _previousValues =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<ILiveSetting> _updatedSettings = [];

    public void Apply(ILiveSetting setting, object? value)
    {
        Record(setting);
        setting.SetObject(value);
    }

    public void Apply(ICoercibleLiveSetting setting, object? value)
    {
        Record(setting);
        setting.ApplyCoerced(value);
    }

    public void Record(ILiveSetting setting)
    {
        parent?.Record(setting);
        if (_previousValues.TryAdd(setting, setting.CurrentValue))
        {
            _updatedSettings.Add(setting);
        }
    }

    public void RollBack()
    {
        for (var i = _updatedSettings.Count - 1; i >= 0; i--)
        {
            var setting = _updatedSettings[i];
            try
            {
                setting.SetObject(_previousValues[setting]);
            }
            catch
            {
                // A slot can reject rollback too; preserve the original failure.
            }
        }
    }
}
