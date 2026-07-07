using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Input;

internal static class PluginEventAdapterValueValidator
{
    public static void ValidateValues(
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<SandboxValue> values)
    {
        if (values is null)
        {
            throw CreateException("Plugin event adapter values must be non-null.");
        }

        EnsureValueCountMatches(ReadValueCount(values), parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
        {
            RequireType(ReadValue(values, i), parameters[i], i);
        }
    }

    public static SandboxValue[] CopyValidatedValues(
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<SandboxValue> values)
    {
        if (values is null)
        {
            throw CreateException("Plugin event adapter values must be non-null.");
        }

        var valueCount = ReadValueCount(values);
        EnsureValueCountMatches(valueCount, parameters.Count);
        var copy = new SandboxValue[valueCount];
        for (var i = 0; i < valueCount; i++)
        {
            var value = ReadValue(values, i);
            RequireType(value, parameters[i], i);
            copy[i] = value;
        }

        return copy;
    }

    public static void ValidateValue(
        IReadOnlyList<Parameter> parameters,
        int eventValueCount,
        int index,
        SandboxValue value)
    {
        ValidateValueCount(parameters, eventValueCount);
        if ((uint)index >= (uint)parameters.Count)
        {
            throw CreateException("Plugin event adapter value index is outside adapter parameters.");
        }

        RequireType(value, parameters[index], index);
    }

    public static void ValidateCopiedValues<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        SandboxValue[] values,
        int destinationIndex)
    {
        var parameters = writer.Parameters;
        ValidateCopiedValues(parameters, writer.EventValueCount, values, destinationIndex);
    }

    public static void ValidateCopiedValues(
        IReadOnlyList<Parameter> parameters,
        int eventValueCount,
        SandboxValue[] values,
        int destinationIndex)
    {
        ValidateValueCount(parameters, eventValueCount);
        for (var i = 0; i < parameters.Count; i++)
        {
            RequireType(values[destinationIndex + i], parameters[i], i);
        }
    }

    public static void ValidateValueCount(IReadOnlyList<Parameter> parameters, int eventValueCount)
        => EnsureValueCountMatches(eventValueCount, parameters.Count);

    private static void EnsureValueCountMatches(int valueCount, int parameterCount)
    {
        if (valueCount != parameterCount)
        {
            throw CreateException("Plugin event adapter value count does not match adapter parameters.");
        }
    }

    private static int ReadValueCount(IReadOnlyList<SandboxValue> values)
    {
        try
        {
            return values.Count;
        }
        catch (Exception ex) when (PluginEventAdapterShapeValidator.IsAdapterCallbackFailure(ex))
        {
            throw CreateException("Plugin event adapter output count could not be read.");
        }
    }

    private static SandboxValue ReadValue(IReadOnlyList<SandboxValue> values, int index)
    {
        try
        {
            return values[index];
        }
        catch (Exception ex) when (PluginEventAdapterShapeValidator.IsAdapterCallbackFailure(ex))
        {
            throw CreateException(
                "Plugin event adapter output at index " +
                index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " could not be read.");
        }
    }

    private static void RequireType(SandboxValue value, Parameter parameter, int index)
    {
        var message =
            "Plugin event adapter output for parameter '" +
            parameter.Name +
            "' at index " +
            index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " does not match adapter parameter type '" +
            parameter.Type +
            "'.";

        try
        {
            SandboxValueValidator.RequireType(value, parameter.Type, SandboxErrorCode.InvalidInput, message);
        }
        catch (SandboxRuntimeException)
        {
            throw CreateException(message);
        }
    }

    private static SandboxValidationException CreateException(string message) =>
        new([
            new SandboxDiagnostic(PluginEventAdapterShapeValidator.DiagnosticCode, message)
        ]);
}
