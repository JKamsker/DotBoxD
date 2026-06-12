namespace SafeIR.Validation.Internal;

using SafeIR;

public sealed record ModuleValidationResult(
    bool Succeeded,
    IReadOnlyList<SandboxDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, FunctionAnalysis> Functions,
    SandboxEffect ModuleEffects,
    IReadOnlySet<string> RequiredCapabilities,
    IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferences)
{
    public static ModuleValidationResult Failure(IReadOnlyList<SandboxDiagnostic> diagnostics)
        => new(
            false,
            diagnostics,
            new Dictionary<string, FunctionAnalysis>(),
            SandboxEffect.None,
            new HashSet<string>(),
            new Dictionary<string, IReadOnlySet<string>>());
}
