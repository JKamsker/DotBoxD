using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Http.Internal;

using System.Globalization;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels;

internal static class SafeHttpGrantReader
{
    private static readonly ConditionalWeakTable<CapabilityGrant, SafeHttpGrantOptions> Cache = new();

    public static SafeHttpGrantOptions Read(CapabilityGrant grant)
        => Cache.GetValue(grant, CreateOptions);

    private static SafeHttpGrantOptions CreateOptions(CapabilityGrant grant)
        => new(
            ReadSet(grant, "allowedSchemes", ["https"]),
            new SafeHttpAllowedAuthorityIndex(ReadValues(grant, "allowedHosts", [])),
            ReadOptionalLong(grant, "maxRequestBytes"),
            ReadRequiredLong(grant, "maxResponseBytes"),
            ReadTimeout(grant),
            ReadBool(grant, "allowIpLiterals"),
            ReadBool(grant, "allowPrivateNetwork"));

    private static HashSet<string> ReadSet(CapabilityGrant grant, string key, string[] fallback)
        => ReadValues(grant, key, fallback).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string[] ReadValues(CapabilityGrant grant, string key, string[] fallback)
    {
        if (!grant.Parameters.TryGetValue(key, out var text))
        {
            return fallback;
        }

        var values = text.Split(',', StringSplitOptions.TrimEntries);
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw Error($"parameter '{key}' is invalid");
        }

        return values;
    }

    private static bool ReadBool(CapabilityGrant grant, string key)
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

    private static long? ReadOptionalLong(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return null;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw Error($"parameter '{key}' is invalid");
        }

        return parsed;
    }

    private static long ReadRequiredLong(CapabilityGrant grant, string key)
        => ReadOptionalLong(grant, key) ?? throw Error($"parameter '{key}' is required");

    private static TimeSpan ReadTimeout(CapabilityGrant grant)
    {
        var milliseconds = ReadOptionalLong(grant, "timeoutMs") ?? 2_000;
        if (milliseconds <= 0 || milliseconds > 60_000)
        {
            throw Error("timeout is outside the allowed range");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static SandboxRuntimeException Error(string message)
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, $"net.http.get denied: {message}"));
}

internal sealed record SafeHttpGrantOptions(
    IReadOnlySet<string> AllowedSchemes,
    SafeHttpAllowedAuthorityIndex AllowedAuthorities,
    long? MaxRequestBytes,
    long MaxResponseBytes,
    TimeSpan Timeout,
    bool AllowIpLiterals,
    bool AllowPrivateNetwork);
