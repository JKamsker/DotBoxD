using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal static class PolicyResolver
{
    public static void Validate(
        SandboxModule module,
        IBindingCatalog bindings,
        SandboxPolicy? policy,
        SandboxEffect requiredEffects,
        IReadOnlySet<string> requiredCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        if (policy is null)
        {
            return;
        }

        ValidateKnownEffects(policy, diagnostics);

        if (!requiredEffects.ContainsOnlyKnownBits())
        {
            diagnostics.Add(new SandboxDiagnostic("E-POLICY-EFFECT", "module has declared unknown effects"));
        }

        PolicyGrantValidator.Validate(
            policy,
            bindings,
            requiredCapabilities,
            module.CapabilityRequests,
            diagnostics);

        // Capture the grant clock once so every membership probe in this validation pass
        // shares a single consistent snapshot instead of re-reading DateTimeOffset.UtcNow
        // per requested/required capability over the cached grant index.
        var now = policy.GrantClock;
        ValidateRequestedCapabilities(module, policy, now, diagnostics);
        ValidateRequiredCapabilities(requiredCapabilities, policy, now, diagnostics);
        ValidateDeniedEffects(requiredEffects, policy, diagnostics);
        ValidateDeterministicPolicy(policy, requiredEffects, now, diagnostics);
    }

    private static void ValidateKnownEffects(SandboxPolicy policy, List<SandboxDiagnostic> diagnostics)
    {
        if (!policy.AllowedEffects.ContainsOnlyKnownBits())
        {
            diagnostics.Add(new SandboxDiagnostic("E-POLICY-EFFECT", "policy declares unknown effects"));
        }
    }

    private static void ValidateRequestedCapabilities(
        SandboxModule module,
        SandboxPolicy policy,
        DateTimeOffset now,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var request in module.CapabilityRequests)
        {
            if (!policy.GrantsCapability(request.Id, now))
            {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-CAP", $"requested capability '{request.Id}' is not granted"));
            }
        }
    }

    private static void ValidateRequiredCapabilities(
        IReadOnlySet<string> requiredCapabilities,
        SandboxPolicy policy,
        DateTimeOffset now,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var capability in requiredCapabilities)
        {
            if (!policy.GrantsCapability(capability, now))
            {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-CAP", $"required capability '{capability}' is not granted"));
            }
        }
    }

    private static void ValidateDeniedEffects(
        SandboxEffect requiredEffects,
        SandboxPolicy policy,
        List<SandboxDiagnostic> diagnostics)
    {
        var deniedEffects = requiredEffects & ~policy.AllowedEffects;
        if (deniedEffects != SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic("E-POLICY-EFFECT", $"policy denies declared effects {deniedEffects}"));
        }
    }

    private static void ValidateDeterministicPolicy(
        SandboxPolicy policy,
        SandboxEffect requiredEffects,
        DateTimeOffset now,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!policy.Deterministic)
        {
            return;
        }

        if (policy.GrantsCapability(RuntimeCapabilityIds.Async, now))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-DETERMINISM",
                "deterministic policy cannot grant runtime async until serialized async limits are configurable"));
        }

        if ((requiredEffects & SandboxEffect.Time) != 0 && policy.LogicalNow is null)
        {
            diagnostics.Add(new SandboxDiagnostic("E-POLICY-DETERMINISM", "deterministic policy requires logical time for Time effects"));
        }

        if ((requiredEffects & SandboxEffect.Random) != 0 && policy.RandomSeed is null)
        {
            diagnostics.Add(new SandboxDiagnostic("E-POLICY-DETERMINISM", "deterministic policy requires a random seed for Random effects"));
        }

        var externalEffects = ExternalEffects(requiredEffects | policy.AllowedEffects);
        if (externalEffects != SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-DETERMINISM",
                $"deterministic policy denies external effects {externalEffects}"));
        }
    }

    private static SandboxEffect ExternalEffects(SandboxEffect effects)
        => effects & (
            SandboxEffect.FileRead |
            SandboxEffect.FileWrite |
            SandboxEffect.Network |
            SandboxEffect.HostStateRead |
            SandboxEffect.HostStateWrite);
}
