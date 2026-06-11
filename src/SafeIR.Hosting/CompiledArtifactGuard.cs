namespace SafeIR.Hosting;

using SafeIR;
using SafeIR.Compiler;

internal static class CompiledArtifactGuard
{
    public static void EnsureMatchesPlan(CompiledArtifact artifact, ExecutionPlan plan)
    {
        if (!Enum.IsDefined(artifact.RuntimeForm)) {
            throw Invalid("compiled artifact runtime form is not supported");
        }

        if (artifact.RuntimeForm == CompiledRuntimeFormKind.DynamicMethod && artifact.AssemblyBytes.Length != 0) {
            throw Invalid("dynamic compiled artifact exposed assembly bytes");
        }

        if (artifact.RuntimeForm == CompiledRuntimeFormKind.LoadedAssembly && artifact.AssemblyBytes.Length == 0) {
            throw Invalid("loaded compiled artifact did not include assembly bytes");
        }

        if (!artifact.Verification.Succeeded ||
            !StringComparer.Ordinal.Equals(artifact.AssemblyHash, artifact.Verification.AssemblyHash) ||
            !StringComparer.Ordinal.Equals(artifact.AssemblyHash, artifact.Manifest.AssemblyHash)) {
            throw Invalid("compiled artifact verification does not match artifact hash");
        }

        if (artifact.Manifest.ModuleHash != plan.ModuleHash ||
            artifact.Manifest.PlanHash != plan.PlanHash ||
            artifact.Manifest.PolicyHash != plan.PolicyHash ||
            artifact.Manifest.BindingManifestHash != plan.BindingManifestHash) {
            throw Invalid("compiled artifact manifest does not match execution plan");
        }
    }

    private static SandboxRuntimeException Invalid(string message)
        => new(new SandboxError(SandboxErrorCode.ValidationError, message));
}
