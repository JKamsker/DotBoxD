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

        if (!TryReadTargetValues(value, out _, out var error))
        {
            Add(diagnostics, grant, TargetListErrorMessage(key, error));
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
        return TryReadTargetValues(value, out var values, out _)
            ? values
            : throw InvalidGrant(key);
    }

    private static int? ReadMaxMessageLength(CapabilityGrant grant)
    {
        if (!grant.Parameters.TryGetValue("maxMessageLength", out var value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw InvalidGrant("maxMessageLength");
        }

        return parsed;
    }

    private static bool TryReadTargetValues(
        string? value,
        out string[] values,
        out TargetValuesValidationError error)
    {
        if (value is null)
        {
            values = [];
            error = TargetValuesValidationError.Null;
            return false;
        }

        values = value.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Any(string.IsNullOrEmpty))
        {
            error = TargetValuesValidationError.EmptyValue;
            return false;
        }

        if (values.Any(item => !SandboxLiteralConstraints.IsOpaqueId(item)))
        {
            error = TargetValuesValidationError.InvalidOpaqueId;
            return false;
        }

        error = TargetValuesValidationError.None;
        return true;
    }

    private static string TargetListErrorMessage(string key, TargetValuesValidationError error)
        => error switch
        {
            TargetValuesValidationError.Null => $"parameter '{key}' must not be null",
            TargetValuesValidationError.EmptyValue => $"parameter '{key}' must not contain empty values",
            TargetValuesValidationError.InvalidOpaqueId => $"parameter '{key}' must contain only opaque target IDs",
            _ => $"parameter '{key}' is invalid"
        };

    private static SandboxRuntimeException InvalidGrant(string key)
        => new(new SandboxError(
            SandboxErrorCode.PermissionDenied,
            $"host.message.send denied: {key} grant is invalid"));

    private enum TargetValuesValidationError
    {
        None,
        Null,
        EmptyValue,
        InvalidOpaqueId
    }
}

internal sealed record PluginMessageGrantOptions(
    IReadOnlySet<string>? AllowedTargets,
    IReadOnlyList<string>? TargetPrefixes,
    int? MaxMessageLength)
{
    public bool AllowsTarget(string targetId)
    {
        if (HasNoTargetRestrictions())
        {
            return true;
        }

        if (IsExplicitlyAllowedTarget(targetId))
        {
            return true;
        }

        return HasAllowedPrefix(targetId);
    }

    private bool HasNoTargetRestrictions()
        => AllowedTargets is null && TargetPrefixes is null;

    private bool IsExplicitlyAllowedTarget(string targetId)
        => AllowedTargets is not null && AllowedTargets.Contains(targetId);

    private bool HasAllowedPrefix(string targetId)
    {
        if (TargetPrefixes is null)
        {
            return false;
        }

        for (var i = 0; i < TargetPrefixes.Count; i++)
        {
            if (IsAllowedPrefixMatch(targetId, TargetPrefixes[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedPrefixMatch(string targetId, string prefix)
    {
        if (!targetId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return IsPrefixBoundary(targetId, prefix);
    }

    private static bool IsPrefixBoundary(string targetId, string prefix)
    {
        if (targetId.Length == prefix.Length)
        {
            return true;
        }

        if (IsTargetSeparator(prefix[prefix.Length - 1]))
        {
            return true;
        }

        return IsTargetSeparator(targetId[prefix.Length]);
    }

    private static bool IsTargetSeparator(char value)
        => value is '.' or ':';
}
