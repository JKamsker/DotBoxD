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
        var requiredCapabilities = RequiredCapabilities(module, bindings);
        PolicyResolver.Validate(module, bindings, policy, requiredEffects, requiredCapabilities, diagnostics);

        return new ModuleValidationResult(
            diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            diagnostics,
            functions,
            requiredEffects,
            requiredCapabilities);
    }

    private static SandboxEffect RequiredEffects(
        SandboxModule module,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
        => module.Functions
            .Where(function => function.IsEntrypoint)
            .Aggregate(
                SandboxEffect.None,
                (current, function) => current | functions[function.Id].Effects);

    private static IReadOnlySet<string> RequiredCapabilities(SandboxModule module, IBindingCatalog bindings)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions.Where(function => function.IsEntrypoint))
        {
            foreach (var bindingId in BindingReferenceCollector.Collect(module, bindings, function.Id))
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
}
