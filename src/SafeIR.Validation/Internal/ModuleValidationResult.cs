namespace SafeIR.Validation;

using SafeIR;

public sealed record ModuleValidationResult(
    bool Succeeded,
    IReadOnlyList<SandboxDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, FunctionAnalysis> Functions,
    SandboxEffect ModuleEffects,
    IReadOnlySet<string> RequiredCapabilities)
{
    public static ModuleValidationResult Failure(IReadOnlyList<SandboxDiagnostic> diagnostics)
        => new(false, diagnostics, new Dictionary<string, FunctionAnalysis>(), SandboxEffect.None, new HashSet<string>());
}
