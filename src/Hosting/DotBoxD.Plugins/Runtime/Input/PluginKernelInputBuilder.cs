using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Plugins.Runtime.Input;

internal static class PluginKernelInputBuilder
{
    public static SandboxValue Build<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<Action> deferredUpdates,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        Action<Action> enqueueUpdate)
    {
        var input = adapter is IPluginEventValueWriter<TEvent> writer
            ? Build(writer, e, parameters, liveSettings, value)
            : Build(adapter, e, parameters, liveSettings, value);

        foreach (var update in deferredUpdates)
        {
            enqueueUpdate(update);
        }

        return input;
    }

    public static SandboxValue BuildWithReusableBuffer<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<Action> deferredUpdates,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        Action<Action> enqueueUpdate,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var input = adapter is IPluginEventValueWriter<TEvent> writer
            ? BuildWithReusableBuffer(writer, e, parameters, liveSettings, value, ref buffer, ref list)
            : BuildWithReusableBuffer(adapter, e, parameters, liveSettings, value, ref buffer, ref list);

        foreach (var update in deferredUpdates)
        {
            enqueueUpdate(update);
        }

        return input;
    }

    private static SandboxValue Build(
        SandboxValue[] eventValues,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var valueCount = eventValues.Length + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValues.Length == 1 ? eventValues[0] : value.ToSandboxValue(liveSettings[0]),
            _ => BuildList(eventValues, liveSettings, value)
        };
    }

    private static SandboxValue Build<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var rawEventValues = adapter.ToSandboxValues(e);
        PluginEventAdapterValueValidator.ValidateValues(parameters, rawEventValues);
        var eventValues = PluginEventAdapterValueValidator.CopyValidatedValues(parameters, rawEventValues);
        return Build(eventValues, liveSettings, value);
    }

    private static SandboxValue Build<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var eventValueCount = PluginEventAdapterShapeValidator.ReadEventValueCount(writer);
        PluginEventAdapterValueValidator.ValidateValueCount(parameters, eventValueCount);
        var valueCount = eventValueCount + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValueCount == 1
                ? ReadWriterValue(writer, e, parameters, eventValueCount, 0)
                : value.ToSandboxValue(liveSettings[0]),
            _ => BuildList(writer, e, parameters, eventValueCount, liveSettings, value)
        };
    }

    private static SandboxValue BuildList(
        SandboxValue[] eventValues,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        if (liveSettings.Count == 0)
        {
            return SandboxValue.FromOwnedList(eventValues, eventValues[0].Type);
        }

        var values = new SandboxValue[eventValues.Length + liveSettings.Count];
        for (var i = 0; i < eventValues.Length; i++)
        {
            values[i] = eventValues[i];
        }

        value.CopySandboxValues(liveSettings, values, eventValues.Length);
        return SandboxValue.FromOwnedList(values, values[0].Type);
    }

    private static SandboxValue BuildList<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        int eventValueCount,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var values = new SandboxValue[eventValueCount + liveSettings.Count];
        CopyWriterValues(writer, e, values, 0);
        PluginEventAdapterValueValidator.ValidateCopiedValues(parameters, eventValueCount, values, 0);
        if (liveSettings.Count > 0)
        {
            value.CopySandboxValues(liveSettings, values, eventValueCount);
        }

        return SandboxValue.FromOwnedList(values, values[0].Type);
    }

    private static SandboxValue BuildWithReusableBuffer(
        SandboxValue[] eventValues,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var valueCount = eventValues.Length + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValues.Length == 1 ? eventValues[0] : value.ToSandboxValue(liveSettings[0]),
            _ => BuildListWithReusableBuffer(eventValues, liveSettings, value, ref buffer, ref list)
        };
    }

    private static SandboxValue BuildWithReusableBuffer<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var rawEventValues = adapter.ToSandboxValues(e);
        PluginEventAdapterValueValidator.ValidateValues(parameters, rawEventValues);
        var eventValues = PluginEventAdapterValueValidator.CopyValidatedValues(parameters, rawEventValues);
        return BuildWithReusableBuffer(eventValues, liveSettings, value, ref buffer, ref list);
    }

    private static SandboxValue BuildWithReusableBuffer<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var eventValueCount = PluginEventAdapterShapeValidator.ReadEventValueCount(writer);
        PluginEventAdapterValueValidator.ValidateValueCount(parameters, eventValueCount);
        var valueCount = eventValueCount + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValueCount == 1
                ? ReadWriterValue(writer, e, parameters, eventValueCount, 0)
                : value.ToSandboxValue(liveSettings[0]),
            _ => BuildListWithReusableBuffer(writer, e, parameters, eventValueCount, liveSettings, value, ref buffer, ref list)
        };
    }

    private static SandboxValue BuildListWithReusableBuffer(
        SandboxValue[] eventValues,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var values = RentBuffer(eventValues.Length + liveSettings.Count, ref buffer);
        for (var i = 0; i < eventValues.Length; i++)
        {
            values[i] = eventValues[i];
        }

        value.CopySandboxValues(liveSettings, values, eventValues.Length);
        return ReusableList(values, values[0].Type, ref list);
    }

    private static SandboxValue BuildListWithReusableBuffer<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        int eventValueCount,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var values = RentBuffer(eventValueCount + liveSettings.Count, ref buffer);
        CopyWriterValues(writer, e, values, 0);
        PluginEventAdapterValueValidator.ValidateCopiedValues(parameters, eventValueCount, values, 0);
        if (liveSettings.Count > 0)
        {
            value.CopySandboxValues(liveSettings, values, eventValueCount);
        }

        return ReusableList(values, values[0].Type, ref list);
    }

    private static SandboxValue[] RentBuffer(int valueCount, ref SandboxValue[]? buffer)
        => buffer is { Length: var length } values && length == valueCount
            ? values
            : buffer = new SandboxValue[valueCount];

    private static SandboxValue ReadWriterValue<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        int eventValueCount,
        int index)
    {
        var eventValue = ReadWriterValue(writer, e, index);
        PluginEventAdapterValueValidator.ValidateValue(parameters, eventValueCount, index, eventValue);
        return eventValue;
    }

    private static SandboxValue ReadWriterValue<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        int index)
    {
        try
        {
            return writer.ToSandboxValue(e, index);
        }
        catch (Exception ex) when (PluginEventAdapterShapeValidator.IsAdapterCallbackFailure(ex))
        {
            throw PluginEventAdapterShapeValidator.CallbackException(nameof(IPluginEventValueWriter<TEvent>.ToSandboxValue));
        }
    }

    private static void CopyWriterValues<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        SandboxValue[] destination,
        int destinationIndex)
    {
        try
        {
            writer.CopySandboxValues(e, destination, destinationIndex);
        }
        catch (Exception ex) when (PluginEventAdapterShapeValidator.IsAdapterCallbackFailure(ex))
        {
            throw PluginEventAdapterShapeValidator.CallbackException(nameof(IPluginEventValueWriter<TEvent>.CopySandboxValues));
        }
    }

    private static ListValue ReusableList(
        SandboxValue[] values,
        SandboxType itemType,
        ref ListValue? list)
    {
        if (list is null || list.Count != values.Length || !list.ItemType.Equals(itemType))
        {
            list = (ListValue)SandboxValue.FromOwnedList(values, itemType);
            return list;
        }

        list.ResetOwnedValues(values);
        return list;
    }
}
