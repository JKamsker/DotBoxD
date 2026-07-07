using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal static class BindingValidationPhases
{
    public static void ValidateBindingIdentity(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        BindingRegistryValidator.ValidateIdentifier(binding.Id, "binding id", "E-BINDING-ID", diagnostics);
        if (binding.RequiredCapability is not null)
        {
            BindingRegistryValidator.ValidateIdentifier(
                binding.RequiredCapability,
                "required capability",
                "E-BINDING-CAP",
                diagnostics);
        }
    }

    public static void ValidateBindingEffectBits(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!binding.Effects.ContainsOnlyKnownBits())
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-EFFECT", $"binding '{binding.Id}' declares an unknown effect"));
        }

        if (binding.Effects == SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-EFFECT", $"binding '{binding.Id}' declares no effects"));
        }
    }

    public static void ValidateBindingClassifications(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
        => BindingClassificationValidator.Validate(
            binding.Id,
            binding.AuditLevel,
            binding.AuditKind,
            binding.Safety,
            diagnostics);

    public static void ValidateCapabilityRequirements(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Effects.RequiresCapability() && string.IsNullOrWhiteSpace(binding.RequiredCapability))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-CAP", $"binding '{binding.Id}' has side effects but no capability"));
        }

        if (!string.IsNullOrWhiteSpace(binding.RequiredCapability) &&
            (binding.Effects & ~SandboxEffects.Pure) == SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-EFFECT",
                $"binding '{binding.Id}' requires a capability but declares only pure effects"));
        }
    }

    public static void ValidateSandboxReach(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!BindingRegistryValidator.ReachesOutsideSandbox(binding))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(binding.RequiredCapability))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-CAP", $"binding '{binding.Id}' reaches outside the sandbox but has no capability"));
        }

        if (binding.AuditLevel == AuditLevel.None)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-AUDIT", $"binding '{binding.Id}' reaches outside the sandbox but is not audited"));
        }
    }

    public static void ValidateCustomCapabilityGrant(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(binding.RequiredCapability) &&
            !BindingRegistryValidator.BuiltInCapabilities.Contains(binding.RequiredCapability) &&
            binding.GrantValidator is null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-GRANT",
                $"binding '{binding.Id}' uses custom capability '{binding.RequiredCapability}' without a grant validator"));
        }
    }

    public static void ValidateDangerousBinding(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Safety == BindingSafety.DangerousRequiresReview)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-DANGER", $"binding '{binding.Id}' is dangerous and cannot be enabled by default"));
        }
    }

    public static void ValidateBindingTypes(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var type in binding.Parameters)
        {
            BindingRegistryValidator.ValidateType(binding, type, diagnostics);
        }

        BindingRegistryValidator.ValidateType(binding, binding.ReturnType, diagnostics);
    }
}
