namespace SafeIR.Validation;

using SafeIR;

internal static class PolicyResolver
{
    public static void Validate(
        SandboxModule module,
        SandboxPolicy? policy,
        IReadOnlyDictionary<string, FunctionAnalysis> functions,
        IReadOnlySet<string> requiredCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        if (policy is null) {
            return;
        }

        foreach (var request in module.CapabilityRequests) {
            if (!policy.GrantsCapability(request.Id)) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-CAP", $"requested capability '{request.Id}' is not granted"));
            }
        }

        foreach (var capability in requiredCapabilities) {
            if (!policy.GrantsCapability(capability)) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-CAP", $"required capability '{capability}' is not granted"));
            }
        }

        var requiredEffects = functions.Values.Aggregate(SandboxEffect.None, (current, next) => current | next.Effects);
        var deniedEffects = requiredEffects & ~policy.AllowedEffects;
        if (deniedEffects != SandboxEffect.None) {
            diagnostics.Add(new SandboxDiagnostic("E-POLICY-EFFECT", $"policy denies effects {deniedEffects}"));
        }

        if (policy.Deterministic) {
            var nondeterministic = requiredEffects & (SandboxEffect.Time | SandboxEffect.Random | SandboxEffect.Network);
            if (nondeterministic != SandboxEffect.None) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-DETERMINISM", $"deterministic policy denies {nondeterministic}"));
            }
        }
    }
}
