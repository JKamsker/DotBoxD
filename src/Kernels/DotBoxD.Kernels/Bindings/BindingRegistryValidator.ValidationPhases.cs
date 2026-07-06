using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal static partial class BindingRegistryValidator
{
    private static void ValidateBindingIdentity(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        ValidateIdentifier(binding.Id, "binding id", "E-BINDING-ID", diagnostics);
        if (binding.RequiredCapability is not null)
        {
            ValidateIdentifier(binding.RequiredCapability, "required capability", "E-BINDING-CAP", diagnostics);
        }
    }

    private static void ValidateBindingEffectBits(
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

    private static void ValidateBindingClassifications(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
        => BindingClassificationValidator.Validate(
            binding.Id,
            binding.AuditLevel,
            binding.AuditKind,
            binding.Safety,
            diagnostics);

    private static void ValidateCapabilityRequirements(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Effects.RequiresCapability() && string.IsNullOrWhiteSpace(binding.RequiredCapability))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-CAP", $"binding '{binding.Id}' has side effects but no capability"));
        }

        if (!string.IsNullOrWhiteSpace(binding.RequiredCapability) &&
            (binding.Effects & ~SandboxEffect.Cpu) == SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-EFFECT",
                $"binding '{binding.Id}' requires a capability but declares only pure CPU effects"));
        }
    }

    private static void ValidateSandboxReach(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!ReachesOutsideSandbox(binding))
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

    private static void ValidateCustomCapabilityGrant(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(binding.RequiredCapability) &&
            !BuiltInCapabilities.Contains(binding.RequiredCapability) &&
            binding.GrantValidator is null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-GRANT",
                $"binding '{binding.Id}' uses custom capability '{binding.RequiredCapability}' without a grant validator"));
        }
    }

    private static void ValidateDangerousBinding(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Safety == BindingSafety.DangerousRequiresReview)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-DANGER", $"binding '{binding.Id}' is dangerous and cannot be enabled by default"));
        }
    }

    private static void ValidateBindingTypes(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var type in binding.Parameters)
        {
            ValidateType(binding, type, diagnostics);
        }

        ValidateType(binding, binding.ReturnType, diagnostics);
    }
}
