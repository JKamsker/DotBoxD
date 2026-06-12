namespace SafeIR.Validation;

using SafeIR;

public sealed class ModuleValidator
{
    public ModuleValidationResult Validate(SandboxModule module, IBindingCatalog bindings, SandboxPolicy? policy = null)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        StructuralValidator.Validate(module, diagnostics);
        if (diagnostics.Count > 0) {
            return ModuleValidationResult.Failure(diagnostics);
        }

        var analyzer = new FunctionAnalyzer(module, bindings, diagnostics);
        var functions = analyzer.AnalyzeAll();
        var requiredEffects = RequiredEffects(module, functions);
        var bindingReferences = BindingReferenceCollector.CollectByFunction(module, bindings);
        var requiredCapabilities = RequiredCapabilities(module, bindings, bindingReferences);
        PolicyResolver.Validate(module, bindings, policy, requiredEffects, requiredCapabilities, diagnostics);

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

            if (!bindingReferences.TryGetValue(function.Id, out var references)) {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (bindings.TryGet(bindingId, out var binding) &&
                    binding.RequiredCapability is not null)
                {
                    required.Add(binding.RequiredCapability);
                }
            }
        }

        return required;
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
