namespace DotBoxD.Abstractions;

internal static class PluginIdentityAttributeValidation
{
    public static string? ValidateOptionalId(string? id)
        => id is null ? null : ValidateRequiredId(id);

    public static string ValidateRequiredId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (!HasStablePluginIdBoundary(id) || !HasStablePluginIdBody(id))
        {
            throw new ArgumentException(
                "Plugin id must be a stable identifier: use ASCII letters, digits, '.', '_' or '-', " +
                "start and end with a letter or digit, and do not use empty dot segments.",
                nameof(id));
        }

        return id;
    }

    private static bool HasStablePluginIdBoundary(string value)
        => value.Length <= 128 &&
           IsAsciiLetterOrDigit(value[0]) &&
           IsAsciiLetterOrDigit(value[value.Length - 1]);

    private static bool HasStablePluginIdBody(string value)
    {
        var previousWasDot = false;
        foreach (var ch in value)
        {
            if (ch == '.')
            {
                if (previousWasDot)
                {
                    return false;
                }

                previousWasDot = true;
                continue;
            }

            previousWasDot = false;
            if (!IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char ch)
        => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
