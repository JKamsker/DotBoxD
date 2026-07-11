using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation.Bindings;

internal static class CatalogBindingSignatureValidator
{
    public static void ValidateCatalog(IBindingCatalog bindings, List<SandboxDiagnostic> diagnostics)
    {
        var signatures = bindings.Signatures;
        for (var i = 0; i < signatures.Count; i++)
        {
            var binding = signatures[i];
            if (!BindingDescriptorRequiredFieldValidator.Validate(binding, diagnostics))
            {
                continue;
            }

            BindingClassificationValidator.Validate(
                binding.Id,
                binding.AuditLevel,
                binding.AuditKind,
                binding.Safety,
                diagnostics);
            ValidateCostModel(binding, diagnostics);
        }
    }

    public static void ValidateReferenced(
        SandboxModule module,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        IBindingCatalog bindings,
        List<SandboxDiagnostic> diagnostics)
    {
        ValidateAllReferenced(bindingReferences, bindings, ValidateBindingTypes, diagnostics);
        ValidateAllReferenced(bindingReferences, bindings, BindingCompiledTargetValidator.Validate, diagnostics);
        ValidateAllReferenced(bindingReferences, bindings, ValidateDangerousBinding, diagnostics);
        ValidateEntrypointReferenced(module, bindingReferences, bindings, ValidateBindingSignature, diagnostics);
        ValidateEntrypointReferencedWithCatalog(module, bindingReferences, bindings, ValidateCustomCapabilityGrant, diagnostics);
    }

    private static void ValidateAllReferenced(
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        IBindingCatalog bindings,
        Action<BindingSignature, List<SandboxDiagnostic>> validate,
        List<SandboxDiagnostic> diagnostics)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var references in bindingReferences.Values)
        {
            foreach (var bindingId in references)
            {
                if (visited.Add(bindingId) &&
                    bindings.TryGet(bindingId, out var binding) &&
                    BindingDescriptorRequiredFieldValidator.HasRequiredFields(binding))
                {
                    validate(binding, diagnostics);
                }
            }
        }
    }

    private static void ValidateEntrypointReferenced(
        SandboxModule module,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        IBindingCatalog bindings,
        Action<BindingSignature, List<SandboxDiagnostic>> validate,
        List<SandboxDiagnostic> diagnostics)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            if (!function.IsEntrypoint ||
                !bindingReferences.TryGetValue(function.Id, out var references))
            {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (visited.Add(bindingId) &&
                    bindings.TryGet(bindingId, out var binding) &&
                    BindingDescriptorRequiredFieldValidator.HasRequiredFields(binding))
                {
                    validate(binding, diagnostics);
                }
            }
        }
    }

    private static void ValidateEntrypointReferencedWithCatalog(
        SandboxModule module,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        IBindingCatalog bindings,
        Action<BindingSignature, IBindingCatalog, List<SandboxDiagnostic>> validate,
        List<SandboxDiagnostic> diagnostics)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            if (!function.IsEntrypoint ||
                !bindingReferences.TryGetValue(function.Id, out var references))
            {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (visited.Add(bindingId) &&
                    bindings.TryGet(bindingId, out var binding) &&
                    BindingDescriptorRequiredFieldValidator.HasRequiredFields(binding))
                {
                    validate(binding, bindings, diagnostics);
                }
            }
        }
    }

    private static void ValidateBindingSignature(BindingSignature binding, List<SandboxDiagnostic> diagnostics)
    {
        if (!binding.Effects.ContainsOnlyKnownBits())
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-EFFECT", $"binding '{binding.Id}' declares an unknown effect"));
        }

        if (binding.Effects == SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-EFFECT", $"binding '{binding.Id}' declares no effects"));
        }

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

        if (IsExternal(binding.Safety) && string.IsNullOrWhiteSpace(binding.RequiredCapability))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-CAP", $"binding '{binding.Id}' reaches outside the sandbox but has no capability"));
        }
    }

    private static void ValidateCustomCapabilityGrant(
        BindingSignature binding,
        IBindingCatalog bindings,
        List<SandboxDiagnostic> diagnostics)
    {
        if (binding.RequiredCapability is null ||
            IsBuiltInCapability(binding.RequiredCapability) ||
            bindings.TryGetCapabilityGrantValidator(binding.RequiredCapability, out _))
        {
            return;
        }

        diagnostics.Add(new SandboxDiagnostic(
            "E-BINDING-GRANT",
            $"binding '{binding.Id}' uses custom capability '{binding.RequiredCapability}' without a grant validator"));
    }

    private static void ValidateBindingTypes(BindingSignature binding, List<SandboxDiagnostic> diagnostics)
    {
        BindingTypeChecks.Validate(binding.Id, binding.ReturnType, diagnostics);
        for (var i = 0; i < binding.Parameters.Count; i++)
        {
            BindingTypeChecks.Validate(binding.Id, binding.Parameters[i], diagnostics);
        }
    }

    private static void ValidateDangerousBinding(BindingSignature binding, List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Safety == BindingSafety.DangerousRequiresReview)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-DANGER",
                $"binding '{binding.Id}' is dangerous and cannot be enabled by default"));
        }
    }

    private static void ValidateCostModel(BindingSignature binding, List<SandboxDiagnostic> diagnostics)
    {
        var cost = binding.CostModel;
        if (cost.BaseFuel < 0 || cost.PerByteFuel < 0 || cost.MaxCallsPerRun is < 0)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COST", $"binding '{binding.Id}' declares a negative resource cost or call limit"));
        }
    }

    private static bool IsExternal(BindingSafety safety)
        => safety is BindingSafety.ReadOnlyExternal or BindingSafety.SideEffectingExternal;

    private static bool IsBuiltInCapability(string capabilityId)
        => capabilityId is "file.read" or "file.write" or "time.now" or "random" or "log.write";
}
