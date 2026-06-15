namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal static class CompiledEntrypointSupport
{
    public static bool CanCompile(ExecutionPlan plan, string entrypoint)
        => plan.FunctionAnalysis.TryGetValue(entrypoint, out var analysis) &&
           IsCompilable(analysis.Effects);

    public static bool TryGetFallbackReason(
        ExecutionPlan plan,
        string entrypoint,
        out SandboxError reason)
    {
        if (plan.FunctionAnalysis.TryGetValue(entrypoint, out var analysis) &&
            !IsCompilable(analysis.Effects))
        {
            reason = new SandboxError(
                SandboxErrorCode.ValidationError,
                "compiled mode supports pure modules only");
            return true;
        }

        reason = null!;
        return false;
    }

    private static bool IsCompilable(SandboxEffect effects)
        => (effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;
}
