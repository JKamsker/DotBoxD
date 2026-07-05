using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Validation;

using DotBoxD.Kernels;
using DotBoxD.Kernels.Validation.Internal;

internal static class PolicyGrantValidator
{
    private static readonly string[] NoAllowedParameterKeys = [];

    public static void Validate(
        SandboxPolicy policy,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyList<CapabilityRequest> requestedCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        var now = policy.GrantClock;
        var grants = policy.Grants;
        AddNullGrantDiagnostics(grants, diagnostics);
        PolicyGrantDuplicateValidator.AddActiveGrantDiagnostics(grants, now, diagnostics);
        foreach (var grant in grants)
        {
            if (grant is not null && IsActive(grant, now))
            {
                ValidateGrant(grant, bindings, requiredCapabilities, requestedCapabilities, diagnostics);
            }
        }
    }

    private static bool IsActive(CapabilityGrant grant, DateTimeOffset now)
        => grant.ExpiresAt is null || grant.ExpiresAt > now;

    private static void AddNullGrantDiagnostics(
        IReadOnlyList<CapabilityGrant> grants,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var grant in grants)
        {
            if (grant is null)
            {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-GRANT", "policy grants cannot contain null entries"));
            }
        }
    }

    private static void ValidateGrant(
        CapabilityGrant grant,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyList<CapabilityRequest> requestedCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        if (grant.Id is null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT",
                "grant id must not be null"));
            return;
        }

        if (grant.Parameters is null)
        {
            Add(diagnostics, grant, "parameter map must not be null");
            return;
        }

        if (CapabilityPattern.IsWildcard(grant.Id))
        {
            ValidateWildcardGrant(grant, bindings, requiredCapabilities, requestedCapabilities, diagnostics);
            return;
        }

        if (grant.Id.StartsWith("event.read.", StringComparison.Ordinal))
        {
            ValidateEventReadGrant(grant, requiredCapabilities, requestedCapabilities, diagnostics);
            return;
        }

        if (ValidateConcreteGrant(grant.Id, grant, bindings, diagnostics))
        {
            return;
        }

        if (!requiredCapabilities.Contains(grant.Id))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT",
                $"grant '{grant.Id}' is not supported by the prepared module"));
        }
    }

    private static void ValidateEventReadGrant(
        CapabilityGrant grant,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyList<CapabilityRequest> requestedCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
        if (!requiredCapabilities.Contains(grant.Id) &&
            !ContainsRequest(requestedCapabilities, grant.Id))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT",
                $"grant '{grant.Id}' is not supported by the prepared module"));
        }
    }

    private static bool ContainsRequest(IReadOnlyList<CapabilityRequest> requests, string capabilityId)
    {
        for (var i = 0; i < requests.Count; i++)
        {
            if (string.Equals(requests[i].Id, capabilityId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateWildcardGrant(
        CapabilityGrant grant,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyList<CapabilityRequest> requestedCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        var matched = false;
        foreach (var required in requiredCapabilities)
        {
            if (!CapabilityPattern.Matches(grant.Id, required))
            {
                continue;
            }

            matched = true;
            ValidateConcreteGrant(required, grant, bindings, diagnostics);
        }

        foreach (var request in requestedCapabilities)
        {
            if (requiredCapabilities.Contains(request.Id) ||
                !CapabilityPattern.Matches(grant.Id, request.Id))
            {
                continue;
            }

            matched = true;
            if (!ValidateConcreteGrant(request.Id, grant, bindings, diagnostics))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT",
                    $"wildcard grant '{grant.Id}' matches requested capability '{request.Id}', but that capability is not supported by the prepared module"));
            }
        }

        if (!matched)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT",
                $"grant '{grant.Id}' is not supported by the prepared module"));
        }
    }

    private static bool ValidateConcreteGrant(
        string capabilityId,
        CapabilityGrant grant,
        IBindingCatalog bindings,
        List<SandboxDiagnostic> diagnostics)
    {
        switch (capabilityId)
        {
            case "file.read":
                FilePolicyGrantValidator.ValidateRead(grant, diagnostics);
                return true;
            case "file.write":
                FilePolicyGrantValidator.ValidateWrite(grant, diagnostics);
                return true;
            case "time.now" or "random" or "log.write" or RuntimeCapabilityIds.Async:
                RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
                return true;
            case RuntimeCapabilityIds.Reentrant:
                RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT",
                    $"grant '{RuntimeCapabilityIds.Reentrant}' is not supported until intra-kernel reentrancy ships"));
                return true;
            default:
                if (capabilityId.StartsWith("event.read.", StringComparison.Ordinal))
                {
                    RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
                    return true;
                }

                if (bindings.TryGetCapabilityGrantValidator(capabilityId, out var validator))
                {
                    validator(grant, diagnostics);
                    return true;
                }

                return false;
        }
    }

    private static void RequireAllowedKeys(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlyList<string> allowedKeys)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            if (!ContainsKey(allowedKeys, key))
            {
                Add(diagnostics, grant, $"parameter '{key}' is not supported");
            }
        }
    }

    private static bool ContainsKey(IReadOnlyList<string> allowedKeys, string key)
    {
        for (var i = 0; i < allowedKeys.Count; i++)
        {
            if (string.Equals(allowedKeys[i], key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(List<SandboxDiagnostic> diagnostics, CapabilityGrant grant, string message)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-POLICY-GRANT-PARAM",
            $"grant '{grant.Id}' {message}"));
}
