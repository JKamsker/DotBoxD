using System.Globalization;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

// File capability grants are stable for the lifetime of a CapabilityGrant instance,
// so the byte limit, create/overwrite flags, and allowed-extension set are decoded
// once per grant and cached. Runtime file bindings reuse the typed options instead of
// reparsing raw CapabilityGrant.Parameters strings (and re-splitting the extension CSV)
// on every file.readText / file.writeText call. Missing or invalid parameters fail closed
// with the same PermissionDenied error on first decode. Mirrors the grant-reader
// caching pattern used by the transport addon's HTTP grant reader.
internal static class SafeFileGrantReader
{
    private static readonly ConditionalWeakTable<CapabilityGrant, SafeFileGrantOptions> Cache = new();

    public static SafeFileGrantOptions Read(CapabilityGrant grant)
        => Cache.GetValue(grant, CreateOptions);

    private static SafeFileGrantOptions CreateOptions(CapabilityGrant grant)
        => new(
            ReadRequiredLong(grant, "maxBytesPerRun"),
            ReadBool(grant, "allowCreate"),
            ReadBool(grant, "allowOverwrite"),
            ReadExtensions(grant));

    private static IReadOnlySet<string>? ReadExtensions(CapabilityGrant grant)
    {
        if (!grant.Parameters.TryGetValue("allowedExtensions", out var allowed) || string.IsNullOrWhiteSpace(allowed))
        {
            return null;
        }

        var extensions = allowed.Split(',', StringSplitOptions.TrimEntries);
        if (extensions.Any(string.IsNullOrWhiteSpace))
        {
            throw Error("allowedExtensions");
        }

        return extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ReadBool(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return false;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw Error(key);
        }

        return parsed;
    }

    private static long ReadRequiredLong(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            throw Error(key);
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw Error(key);
        }

        return parsed;
    }

    private static SandboxRuntimeException Error(string key)
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, $"file grant denied: parameter '{key}' is invalid"));
}

internal sealed record SafeFileGrantOptions(
    long MaxBytesPerRun,
    bool AllowCreate,
    bool AllowOverwrite,
    IReadOnlySet<string>? AllowedExtensions);
