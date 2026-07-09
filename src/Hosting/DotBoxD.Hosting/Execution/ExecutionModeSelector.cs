using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Hosting.Execution;

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

public sealed record ModuleHotnessStats(
    string PlanHash,
    string Entrypoint,
    int RunCount,
    int CompletedRunCount,
    TimeSpan AverageInterpretedDuration,
    long AverageFuelUsed,
    DateTimeOffset? LastRunAt,
    int CompileFailures,
    string? LastCompiledArtifactHash)
{
    private string _planHash = RequireNotNull(PlanHash, nameof(PlanHash));
    private string _entrypoint = RequireNotNull(Entrypoint, nameof(Entrypoint));
    private int _runCount = RequireNonNegative(RunCount, nameof(RunCount));
    private int _completedRunCount = RequireNonNegative(CompletedRunCount, nameof(CompletedRunCount));
    private int _compileFailures = RequireNonNegative(CompileFailures, nameof(CompileFailures));

    public string PlanHash
    {
        get => _planHash;
        init => _planHash = RequireNotNull(value, nameof(PlanHash));
    }

    public string Entrypoint
    {
        get => _entrypoint;
        init => _entrypoint = RequireNotNull(value, nameof(Entrypoint));
    }

    public int RunCount
    {
        get => _runCount;
        init => _runCount = RequireNonNegative(value, nameof(RunCount));
    }

    public int CompletedRunCount
    {
        get => _completedRunCount;
        init => _completedRunCount = RequireNonNegative(value, nameof(CompletedRunCount));
    }

    public int CompileFailures
    {
        get => _compileFailures;
        init => _compileFailures = RequireNonNegative(value, nameof(CompileFailures));
    }

    public ModuleHotnessStats(int runCount)
        : this(
            string.Empty,
            string.Empty,
            runCount,
            0,
            TimeSpan.Zero,
            0,
            null,
            0,
            null)
    {
    }

    private static int RequireNonNegative(int value, string paramName)
        => value >= 0 ? value : throw new ArgumentOutOfRangeException(paramName);

    private static string RequireNotNull(string? value, string paramName)
        => value ?? throw new ArgumentNullException(paramName);
}

public sealed class HotnessExecutionModeSelector : IExecutionModeSelector
{
    public ExecutionModeDecision Choose(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats hotness,
        CompiledCacheStatus cacheStatus)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hotness);

        var threshold = Math.Max(2, options.AutoCompileThreshold);
        return hotness.RunCount < threshold
            ? ExecutionModeDecision.Interpreted
            : ExecutionModeDecision.Compiled;
    }
}
