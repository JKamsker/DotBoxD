namespace SafeIR.Runtime;

using System.Globalization;
using SafeIR;

internal static class SafeHttpGrantReader
{
    public static HashSet<string> ReadSet(CapabilityGrant grant, string key, string[] fallback)
    {
        var text = grant.Parameters.TryGetValue(key, out var value) ? value : string.Join(',', fallback);
        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool ReadBool(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return false;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw Error($"parameter '{key}' is invalid");
        }

        return parsed;
    }

    public static long ReadLong(CapabilityGrant grant, string key, long fallback)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw Error($"parameter '{key}' is invalid");
        }

        return parsed;
    }

    public static TimeSpan ReadTimeout(CapabilityGrant grant)
    {
        var milliseconds = ReadLong(grant, "timeoutMs", 2_000);
        if (milliseconds <= 0 || milliseconds > 60_000)
        {
            throw Error("timeout is outside the allowed range");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static SandboxRuntimeException Error(string message)
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, $"net.http.get denied: {message}"));
}
