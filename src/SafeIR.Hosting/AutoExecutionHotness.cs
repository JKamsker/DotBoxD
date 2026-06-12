namespace SafeIR.Hosting;

using System.Collections.Concurrent;
using SafeIR;

internal sealed class AutoExecutionHotness
{
    private readonly ConcurrentDictionary<string, AutoHotnessState> _states = new(StringComparer.Ordinal);

    public AutoHotnessAttempt BeginAttempt(ExecutionPlan plan, string entrypoint)
    {
        var state = _states.GetOrAdd(
            Key(plan.PlanHash, entrypoint),
            _ => new AutoHotnessState(plan.PlanHash, entrypoint));
        return state.BeginAttempt();
    }

    private static string Key(string planHash, string entrypoint)
        => planHash + "|" + entrypoint;
}

internal sealed class AutoHotnessAttempt(
    AutoHotnessState state,
    ModuleHotnessStats stats)
{
    public ModuleHotnessStats Stats { get; } = stats;

    public void Complete(SandboxExecutionResult result, TimeSpan elapsed)
        => state.RecordResult(result, elapsed);
}

internal sealed class AutoHotnessState(string planHash, string entrypoint)
{
    private readonly object _gate = new();
    private int _runCount;
    private int _completedRunCount;
    private int _interpretedRunCount;
    private long _totalFuelUsed;
    private long _interpretedDurationTicks;
    private DateTimeOffset? _lastRunAt;
    private int _compileFailures;
    private string? _lastCompiledArtifactHash;

    public AutoHotnessAttempt BeginAttempt()
    {
        lock (_gate)
        {
            if (_runCount < int.MaxValue)
            {
                _runCount++;
            }

            return new AutoHotnessAttempt(this, Snapshot());
        }
    }

    public void RecordResult(SandboxExecutionResult result, TimeSpan elapsed)
    {
        lock (_gate)
        {
            if (_completedRunCount < int.MaxValue)
            {
                _completedRunCount++;
            }

            _totalFuelUsed = SaturatingAdd(_totalFuelUsed, result.ResourceUsage.FuelUsed);
            if (result.ActualMode == ExecutionMode.Interpreted)
            {
                if (_interpretedRunCount < int.MaxValue)
                {
                    _interpretedRunCount++;
                }

                _interpretedDurationTicks = SaturatingAdd(_interpretedDurationTicks, elapsed.Ticks);
            }

            if (IsCompileFailure(result) && _compileFailures < int.MaxValue)
            {
                _compileFailures++;
            }

            if (result.ActualMode == ExecutionMode.Compiled && !string.IsNullOrWhiteSpace(result.ArtifactHash))
            {
                _lastCompiledArtifactHash = result.ArtifactHash;
            }

            _lastRunAt = DateTimeOffset.UtcNow;
        }
    }

    private ModuleHotnessStats Snapshot()
    {
        var averageFuel = _completedRunCount == 0 ? 0 : _totalFuelUsed / _completedRunCount;
        var averageInterpretedTicks = _interpretedRunCount == 0 ? 0 : _interpretedDurationTicks / _interpretedRunCount;
        return new ModuleHotnessStats(
            planHash,
            entrypoint,
            _runCount,
            _completedRunCount,
            TimeSpan.FromTicks(averageInterpretedTicks),
            averageFuel,
            _lastRunAt,
            _compileFailures,
            _lastCompiledArtifactHash);
    }

    private static bool IsCompileFailure(SandboxExecutionResult result)
        => !result.Succeeded &&
           ((!result.ExecutionDispatched && result.ActualMode == ExecutionMode.Compiled) ||
            result.AuditEvents.Any(e => e.Kind == "ExecutionFallback"));

    private static long SaturatingAdd(long left, long right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }
}
