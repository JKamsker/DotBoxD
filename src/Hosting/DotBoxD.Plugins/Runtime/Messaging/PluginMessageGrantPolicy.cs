using System.Globalization;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginMessageGrantPolicy
{
    private static readonly string[] AllowedGrantKeys =
        ["allowedTargets", "targetPrefixes", "maxMessageLength"];

    private static readonly ConditionalWeakTable<CapabilityGrant, PluginMessageGrantOptions> OptionsCache = new();

    public static void Validate(CapabilityGrant grant, ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            if (Array.IndexOf(AllowedGrantKeys, key) < 0)
            {
                Add(diagnostics, grant, $"parameter '{key}' is not supported");
            }
        }

        ValidateTargetList(grant, diagnostics, "allowedTargets");
        ValidateTargetList(grant, diagnostics, "targetPrefixes");
        ValidateMaxMessageLength(grant, diagnostics);
    }

    public static PluginMessageGrantOptions ReadOptions(CapabilityGrant grant)
        => OptionsCache.GetValue(grant, CreateGrantOptions);

    private static void ValidateTargetList(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return;
        }

        if (value is null)
        {
            Add(diagnostics, grant, $"parameter '{key}' must not be null");
            return;
        }

        var values = value.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Any(string.IsNullOrEmpty))
        {
            Add(diagnostics, grant, $"parameter '{key}' must not contain empty values");
            return;
        }

        if (values.Any(item => !SandboxLiteralConstraints.IsOpaqueId(item)))
        {
            Add(diagnostics, grant, $"parameter '{key}' must contain only opaque target IDs");
        }
    }

    private static void ValidateMaxMessageLength(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics)
    {
        if (grant.Parameters.TryGetValue("maxMessageLength", out var value) &&
            (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0))
        {
            Add(diagnostics, grant, "parameter 'maxMessageLength' must be a non-negative integer");
        }
    }

    private static void Add(ICollection<SandboxDiagnostic> diagnostics, CapabilityGrant grant, string message)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-POLICY-GRANT-PARAM",
            $"grant '{grant.Id}' {message}"));

    private static PluginMessageGrantOptions CreateGrantOptions(CapabilityGrant grant)
        => new(
            ReadTargetSet(grant, "allowedTargets"),
            ReadTargetList(grant, "targetPrefixes"),
            ReadMaxMessageLength(grant));

    private static IReadOnlySet<string>? ReadTargetSet(CapabilityGrant grant, string key)
        => grant.Parameters.TryGetValue(key, out var value)
            ? ReadTargetValues(value, key).ToHashSet(StringComparer.Ordinal)
            : null;

    private static IReadOnlyList<string>? ReadTargetList(CapabilityGrant grant, string key)
        => grant.Parameters.TryGetValue(key, out var value)
            ? ReadTargetValues(value, key)
            : null;

    private static string[] ReadTargetValues(string? value, string key)
    {
        if (value is null)
        {
            throw InvalidGrant(key);
        }

        var values = value.Split(',', StringSplitOptions.TrimEntries);
        return values.Length == 0 ||
            values.Any(string.IsNullOrEmpty) ||
            values.Any(item => !SandboxLiteralConstraints.IsOpaqueId(item))
            ? throw InvalidGrant(key)
            : values;
    }

    private static int? ReadMaxMessageLength(CapabilityGrant grant)
    {
        if (!grant.Parameters.TryGetValue("maxMessageLength", out var value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.PermissionDenied,
                "host.message.send denied: maxMessageLength grant is invalid"));
        }

        return parsed;
    }

    private static SandboxRuntimeException InvalidGrant(string key)
        => new(new SandboxError(
            SandboxErrorCode.PermissionDenied,
            $"host.message.send denied: {key} grant is invalid"));
}

internal sealed record PluginMessageGrantOptions(
    IReadOnlySet<string>? AllowedTargets,
    IReadOnlyList<string>? TargetPrefixes,
    int? MaxMessageLength)
{
    public bool AllowsTarget(string targetId)
    {
        if (AllowedTargets is null && TargetPrefixes is null)
        {
            return true;
        }

        if (AllowedTargets is not null && AllowedTargets.Contains(targetId))
        {
            return true;
        }

        if (TargetPrefixes is null)
        {
            return false;
        }

        for (var i = 0; i < TargetPrefixes.Count; i++)
        {
            var prefix = TargetPrefixes[i];
            if (targetId.StartsWith(prefix, StringComparison.Ordinal) &&
                (targetId.Length == prefix.Length ||
                 prefix[^1] is '.' or ':' ||
                 targetId[prefix.Length] is '.' or ':'))
            {
                return true;
            }
        }

        return false;
    }
}
