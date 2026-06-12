namespace SafeIR.Hosting;

using SafeIR;
using SafeIR.Compiler;

public interface IExecutionModeSelector
{
    ExecutionModeDecision Choose(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats hotness,
        CompiledCacheStatus cacheStatus);
}

public sealed record ExecutionModeDecision(ExecutionMode Mode)
{
    public static ExecutionModeDecision Interpreted { get; } = new(ExecutionMode.Interpreted);
    public static ExecutionModeDecision Compiled { get; } = new(ExecutionMode.Compiled);
}

public sealed record ModuleHotnessStats(int RunCount);

public sealed class HotnessExecutionModeSelector : IExecutionModeSelector
{
    public ExecutionModeDecision Choose(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats hotness,
        CompiledCacheStatus cacheStatus)
    {
        var threshold = Math.Max(2, options.AutoCompileThreshold);
        return hotness.RunCount < threshold
            ? ExecutionModeDecision.Interpreted
            : ExecutionModeDecision.Compiled;
    }
}
