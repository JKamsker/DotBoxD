namespace DotBoxD.Kernels.Sandbox.Values;

internal static class SandboxValueCollectionValidation
{
    public static SandboxValue[] CopyList(IReadOnlyList<SandboxValue> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = new SandboxValue[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            copy[i] = values[i] ?? throw NullElement(parameterName);
        }

        return copy;
    }

    public static IReadOnlyList<SandboxValue> RequireList(
        IReadOnlyList<SandboxValue> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is null)
            {
                throw NullElement(parameterName);
            }
        }

        return values;
    }

    public static void RequireArray(SandboxValue[] values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is null)
            {
                throw NullElement(parameterName);
            }
        }
    }

    public static IReadOnlyDictionary<SandboxValue, SandboxValue> RequireMap(
        IReadOnlyDictionary<SandboxValue, SandboxValue> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        foreach (var entry in values)
        {
            if (entry.Key is null || entry.Value is null)
            {
                throw NullElement(parameterName);
            }
        }

        return values;
    }

    private static ArgumentException NullElement(string parameterName)
        => new("Sandbox value collections must not contain null elements.", parameterName);
}
