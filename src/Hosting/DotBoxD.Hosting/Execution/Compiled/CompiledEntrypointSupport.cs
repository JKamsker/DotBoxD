using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution.Compiled;

internal static class CompiledEntrypointSupport
{
    public static bool CanCompile(ExecutionPlan plan, string entrypoint)
        => plan.FunctionLookup.TryGetValue(entrypoint, out var function) && function.IsEntrypoint;

    public static void EnsureCanCompile(ExecutionPlan plan, string entrypoint)
    {
        if (!CanCompile(plan, entrypoint))
        {
            throw new SandboxRuntimeException(
                new SandboxError(SandboxErrorCode.ValidationError, $"entrypoint '{entrypoint}' is not available"));
        }
    }
}
