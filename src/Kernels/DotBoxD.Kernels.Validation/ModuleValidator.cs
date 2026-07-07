using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Validation;

using DotBoxD.Kernels;

public sealed class ModuleValidator
{
    private static readonly IReadOnlySet<string> NoDeclaredOpaqueIdTypes =
        new HashSet<string>(StringComparer.Ordinal);

    public ModuleValidationResult Validate(SandboxModule module, IBindingCatalog bindings, SandboxPolicy? policy = null)
        => ValidateCore(
            module,
            bindings,
            policy?.DeclaredOpaqueIdTypes ?? NoDeclaredOpaqueIdTypes,
            policy);

    internal ModuleValidationResult ValidateForCapabilityDiscovery(
        SandboxModule module,
        IBindingCatalog bindings,
        IReadOnlySet<string> declaredOpaqueIdTypes)
        => ValidateCore(module, bindings, declaredOpaqueIdTypes, policy: null);

    private static ModuleValidationResult ValidateCore(
        SandboxModule module,
        IBindingCatalog bindings,
        IReadOnlySet<string> declaredOpaqueIdTypes,
        SandboxPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(bindings);

        var diagnostics = new List<SandboxDiagnostic>();
        StructuralValidator.Validate(module, diagnostics, declaredOpaqueIdTypes);
        ValidateBindingCatalog(bindings, diagnostics);
        if (diagnostics.Count > 0)
        {
            return ModuleValidationResult.Failure(diagnostics);
        }

        IReadOnlyDictionary<string, FunctionAnalysis> functions;
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences;
        IReadOnlySet<string> requiredCapabilities;
        SandboxEffect requiredEffects;
        try
        {
            bindingReferences = BindingReferenceCollector.CollectByFunction(module, bindings);
            ValidateReferencedBindingTypes(bindings, bindingReferences, diagnostics);
            if (diagnostics.Count > 0)
            {
                return ModuleValidationResult.Failure(diagnostics);
            }

            var analyzer = new FunctionAnalyzer(module, bindings, diagnostics, declaredOpaqueIdTypes);
            functions = analyzer.AnalyzeAll();
            requiredEffects = RequiredEffects(module, functions);
            ValidateReferencedBindingSignatures(module, bindings, bindingReferences, diagnostics);
            ValidateReferencedCompiledTargets(bindings, bindingReferences, diagnostics);
            requiredCapabilities = RequiredCapabilities(module, bindings, bindingReferences);
            ValidateCustomCapabilityGrantValidators(module, bindings, bindingReferences, diagnostics);
            PolicyResolver.Validate(module, bindings, policy, requiredEffects, requiredCapabilities, diagnostics);
        }
        catch (SandboxValidationException ex)
        {
            diagnostics.AddRange(ex.Diagnostics);
            return ModuleValidationResult.Failure(diagnostics);
        }

        return new ModuleValidationResult(
            HasNoErrors(diagnostics),
            diagnostics,
            functions,
            requiredEffects,
            requiredCapabilities,
            bindingReferences);
    }

    private static SandboxEffect RequiredEffects(
        SandboxModule module,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
    {
        var effects = SandboxEffect.None;
        foreach (var function in module.Functions)
        {
            if (function.IsEntrypoint)
            {
                effects |= functions[function.Id].Effects;
            }
        }

        return effects;
    }

    private static void ValidateReferencedBindingSignatures(
        SandboxModule module,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        List<SandboxDiagnostic> diagnostics)
    {
        var checkedBindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            if (!function.IsEntrypoint ||
                !bindingReferences.TryGetValue(function.Id, out var references))
            {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (checkedBindings.Add(bindingId) &&
                    bindings.TryGet(bindingId, out var binding))
                {
                    ValidateReferencedBindingSignature(binding, diagnostics);
                }
            }
        }
    }

    private static void ValidateReferencedBindingSignature(
        BindingSignature binding,
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

    private static bool IsExternal(BindingSafety safety)
        => safety is BindingSafety.ReadOnlyExternal or BindingSafety.SideEffectingExternal;

    private static IReadOnlySet<string> RequiredCapabilities(
        SandboxModule module,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            if (!function.IsEntrypoint)
            {
                continue;
            }

            if (!bindingReferences.TryGetValue(function.Id, out var references))
            {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (!bindings.TryGet(bindingId, out var binding))
                {
                    continue;
                }

                if (binding.RequiredCapability is not null)
                {
                    required.Add(binding.RequiredCapability);
                }

                if (RequiresRuntimeAsync(binding))
                {
                    required.Add(RuntimeCapabilityIds.Async);
                }
            }
        }

        return required;
    }

    private static void ValidateReferencedCompiledTargets(
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        List<SandboxDiagnostic> diagnostics)
    {
        var validated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var references in bindingReferences.Values)
        {
            foreach (var bindingId in references)
            {
                if (!validated.Add(bindingId) || !bindings.TryGet(bindingId, out var binding))
                {
                    continue;
                }

                BindingCompiledTargetValidator.Validate(binding, diagnostics);
            }
        }
    }

    private static void ValidateReferencedBindingTypes(
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        List<SandboxDiagnostic> diagnostics)
    {
        var validated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var references in bindingReferences.Values)
        {
            foreach (var bindingId in references)
            {
                if (!validated.Add(bindingId) || !bindings.TryGet(bindingId, out var binding))
                {
                    continue;
                }

                BindingTypeChecks.Validate(binding.Id, binding.ReturnType, diagnostics);
                for (var i = 0; i < binding.Parameters.Count; i++)
                {
                    BindingTypeChecks.Validate(binding.Id, binding.Parameters[i], diagnostics);
                }
            }
        }
    }

    private static void ValidateCustomCapabilityGrantValidators(
        SandboxModule module,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        List<SandboxDiagnostic> diagnostics)
    {
        var checkedBindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            if (!function.IsEntrypoint ||
                !bindingReferences.TryGetValue(function.Id, out var references))
            {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (!checkedBindings.Add(bindingId) ||
                    !bindings.TryGet(bindingId, out var binding) ||
                    binding.RequiredCapability is null ||
                    IsBuiltInCapability(binding.RequiredCapability) ||
                    bindings.TryGetCapabilityGrantValidator(binding.RequiredCapability, out _))
                {
                    continue;
                }

                diagnostics.Add(new SandboxDiagnostic(
                    "E-BINDING-GRANT",
                    $"binding '{binding.Id}' uses custom capability '{binding.RequiredCapability}' without a grant validator"));
            }
        }
    }

    private static bool IsBuiltInCapability(string capabilityId)
        => capabilityId is "file.read" or "file.write" or "time.now" or "random" or "log.write";

    private static bool RequiresRuntimeAsync(BindingSignature binding)
        => binding.IsAsync || (binding.Effects & SandboxEffect.Concurrency) != 0;

    private static void ValidateBindingCatalog(IBindingCatalog bindings, List<SandboxDiagnostic> diagnostics)
    {
        var signatures = bindings.Signatures;
        for (var i = 0; i < signatures.Count; i++)
        {
            var binding = signatures[i];
            BindingClassificationValidator.Validate(
                binding.Id,
                binding.AuditLevel,
                binding.AuditKind,
                binding.Safety,
                diagnostics);
            ValidateCostModel(binding, diagnostics);
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

    private static bool HasNoErrors(IReadOnlyList<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == DiagnosticSeverity.Error)
            {
                return false;
            }
        }

        return true;
    }
}
