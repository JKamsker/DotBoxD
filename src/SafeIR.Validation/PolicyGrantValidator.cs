namespace SafeIR.Validation;

using System.Globalization;
using SafeIR;

internal static class PolicyGrantValidator
{
    public static void Validate(
        SandboxPolicy policy,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        var activeGrants = policy.Grants
            .Where(IsActive)
            .ToArray();
        foreach (var group in activeGrants.GroupBy(g => g.Id, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT",
                    $"capability '{group.Key}' has multiple active grants"));
            }
        }

        foreach (var grant in activeGrants)
        {
            ValidateGrant(grant, bindings, requiredCapabilities, diagnostics);
        }
    }

    private static bool IsActive(CapabilityGrant grant)
        => grant.ExpiresAt is null || grant.ExpiresAt > DateTimeOffset.UtcNow;

    private static void ValidateGrant(
        CapabilityGrant grant,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        switch (grant.Id)
        {
            case "file.read":
                ValidateFileGrant(grant, diagnostics, allowWriteFlags: false);
                break;
            case "file.write":
                ValidateFileGrant(grant, diagnostics, allowWriteFlags: true);
                break;
            case "time.now" or "random" or "log.write":
                RequireAllowedKeys(grant, diagnostics, []);
                break;
            default:
                if (bindings.TryGetCapabilityGrantValidator(grant.Id, out var validator))
                {
                    validator(grant, diagnostics);
                    return;
                }

                if (!requiredCapabilities.Contains(grant.Id))
                {
                    diagnostics.Add(new SandboxDiagnostic(
                        "E-POLICY-GRANT",
                        $"grant '{grant.Id}' is not supported by the prepared module"));
                }

                break;
        }
    }

    private static void ValidateFileGrant(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        bool allowWriteFlags)
    {
        var allowed = allowWriteFlags
            ? new[] { "root", "maxBytesPerRun", "allowCreate", "allowOverwrite", "allowedExtensions" }
            : ["root", "maxBytesPerRun", "allowedExtensions"];
        RequireAllowedKeys(grant, diagnostics, allowed);
        RequireNonEmpty(grant, diagnostics, "root");
        RequireNonNegativeLong(grant, diagnostics, "maxBytesPerRun");
        RequireAllowedExtensions(grant, diagnostics);
        if (allowWriteFlags)
        {
            RequireOptionalBool(grant, diagnostics, "allowCreate");
            RequireOptionalBool(grant, diagnostics, "allowOverwrite");
        }
    }

    private static void RequireAllowedKeys(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        IEnumerable<string> allowedKeys)
    {
        var allowed = allowedKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in grant.Parameters.Keys)
        {
            if (!allowed.Contains(key))
            {
                Add(diagnostics, grant, $"parameter '{key}' is not supported");
            }
        }
    }

    private static void RequireNonEmpty(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            Add(diagnostics, grant, $"parameter '{key}' is required");
        }
    }

    private static void RequireCsv(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value) ||
            value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length == 0)
        {
            Add(diagnostics, grant, $"parameter '{key}' must contain at least one value");
        }
    }

    private static void RequireAllowedExtensions(CapabilityGrant grant, List<SandboxDiagnostic> diagnostics)
    {
        const string key = "allowedExtensions";
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return;
        }

        var extensions = value.Split(',', StringSplitOptions.TrimEntries);
        if (extensions.Length == 0 || extensions.All(string.IsNullOrWhiteSpace))
        {
            Add(diagnostics, grant, $"parameter '{key}' must contain at least one extension");
            return;
        }

        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                Add(diagnostics, grant, $"parameter '{key}' must not contain empty values");
                continue;
            }

            if (!IsValidExtension(extension))
            {
                Add(diagnostics, grant, $"parameter '{key}' contains invalid extension '{extension}'");
            }
        }
    }

    private static void RequireNonNegativeLong(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        string key)
        => RequireRangeLong(grant, diagnostics, key, min: 0, max: long.MaxValue);

    private static void RequireRangeLong(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        string key,
        long min,
        long max)
    {
        if (!grant.Parameters.TryGetValue(key, out var value) ||
            !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < min ||
            parsed > max)
        {
            Add(diagnostics, grant, $"parameter '{key}' must be between {min} and {max}");
        }
    }

    private static void RequireOptionalBool(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (grant.Parameters.TryGetValue(key, out var value) && !bool.TryParse(value, out _))
        {
            Add(diagnostics, grant, $"parameter '{key}' must be a boolean");
        }
    }

    private static void Add(List<SandboxDiagnostic> diagnostics, CapabilityGrant grant, string message)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-POLICY-GRANT-PARAM",
            $"grant '{grant.Id}' {message}"));

    private static bool IsValidExtension(string extension)
        => extension.Length > 1 &&
           extension[0] == '.' &&
           extension.Skip(1).All(c => !char.IsControl(c) && !char.IsWhiteSpace(c) && c is not '/' and not '\\' and not '.');
}
