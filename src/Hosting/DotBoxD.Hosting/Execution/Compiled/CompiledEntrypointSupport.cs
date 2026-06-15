namespace DotBoxD.Hosting.Execution.Compiled;

internal static class CompiledEntrypointSupport
{
    public static bool CanCompile(ExecutionPlan plan, string entrypoint)
        => plan.FunctionAnalysis.ContainsKey(entrypoint);
}
