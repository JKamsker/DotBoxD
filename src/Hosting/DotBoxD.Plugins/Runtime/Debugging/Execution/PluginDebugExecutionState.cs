using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugExecutionState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<SandboxNodeId, PluginDebugBreakpointState>> _breakpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginDebugStoppedExecution> _stopped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StepPlan> _steps = new(StringComparer.Ordinal);
    private bool _pauseRequested;

    public IReadOnlyList<SandboxNodeId> SetBreakpoints(string pluginId, IEnumerable<string> nodeIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(nodeIds);
        var parsed = nodeIds.Select(value => new PluginDebugBreakpointSpec(new SandboxNodeId(value)))
            .DistinctBy(spec => spec.NodeId)
            .ToArray();
        SetBreakpoints(pluginId, parsed);
        return parsed.Select(spec => spec.NodeId).ToArray();
    }

    public void SetBreakpoints(string pluginId, IReadOnlyList<PluginDebugBreakpointSpec> breakpoints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(breakpoints);
        lock (_gate)
        {
            _breakpoints[pluginId] = breakpoints.ToDictionary(
                breakpoint => breakpoint.NodeId,
                breakpoint => new PluginDebugBreakpointState(breakpoint));
        }
    }

    public void RequestPause()
    {
        lock (_gate)
        {
            _pauseRequested = true;
        }
    }

    public PluginDebugCheckpointDecision Decide(string pluginId, SandboxDebugCheckpoint checkpoint)
    {
        lock (_gate)
        {
            if (_pauseRequested)
            {
                _pauseRequested = false;
                _steps.Remove(checkpoint.RunId.ToString());
                return PluginDebugCheckpointDecision.Stop("pause");
            }

            if (checkpoint.Kind == SandboxDebugCheckpointKind.Exception)
            {
                _steps.Remove(checkpoint.RunId.ToString());
                return PluginDebugCheckpointDecision.Stop("exception");
            }

            if (_breakpoints.TryGetValue(pluginId, out var nodes) &&
                nodes.TryGetValue(checkpoint.Node.Id, out var breakpoint))
            {
                breakpoint.Hits++;
                if (breakpoint.Spec.HitCount is null || breakpoint.Hits == breakpoint.Spec.HitCount)
                {
                    _steps.Remove(checkpoint.RunId.ToString());
                    return PluginDebugCheckpointDecision.ForBreakpoint(breakpoint.Spec);
                }
            }

            var runId = checkpoint.RunId.ToString();
            if (_steps.TryGetValue(runId, out var step) && step.ShouldStop(checkpoint))
            {
                _steps.Remove(runId);
                return PluginDebugCheckpointDecision.Stop("step");
            }
        }

        return PluginDebugCheckpointDecision.None;
    }

    public void RecordStopped(string pluginId, SandboxDebugCheckpoint checkpoint, string reason)
    {
        lock (_gate)
        {
            _stopped[checkpoint.RunId.ToString()] = new PluginDebugStoppedExecution(pluginId, checkpoint, reason);
        }
    }

    public bool ContainsStopped(string runId)
    {
        lock (_gate)
        {
            return _stopped.ContainsKey(runId);
        }
    }

    public IReadOnlyList<PluginDebugStoppedExecution> StoppedExecutions()
    {
        lock (_gate)
        {
            return _stopped.Values.ToArray();
        }
    }

    public bool TryGetStopped(string runId, out PluginDebugStoppedExecution? execution)
    {
        lock (_gate)
        {
            return _stopped.TryGetValue(runId, out execution);
        }
    }

    public bool TryGetFrame(string frameId, out ISandboxDebugFrame? frame)
        => TryGetFrame(frameId, out frame, out _);

    public bool TryGetFrame(string frameId, out ISandboxDebugFrame? frame, out string? pluginId)
    {
        var separator = frameId.LastIndexOf(':');
        if (separator <= 0 ||
            !int.TryParse(
                frameId.AsSpan(separator + 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var depth))
        {
            frame = null;
            pluginId = null;
            return false;
        }

        var runId = frameId[..separator];
        lock (_gate)
        {
            if (!_stopped.TryGetValue(runId, out var checkpoint))
            {
                frame = null;
                pluginId = null;
                return false;
            }

            pluginId = checkpoint.PluginId;
            frame = checkpoint.Checkpoint.Frame;
            while (frame is not null && frame.Depth != depth)
            {
                frame = frame.Caller;
            }

            return frame is not null;
        }
    }

    public bool PrepareResume(string runId, PluginDebugStepKind stepKind)
    {
        lock (_gate)
        {
            if (!_stopped.TryGetValue(runId, out var execution))
            {
                return false;
            }

            if (stepKind == PluginDebugStepKind.Continue)
            {
                _steps.Remove(runId);
            }
            else
            {
                _steps[runId] = new StepPlan(stepKind, execution.Checkpoint.Frame.Depth);
            }

            return true;
        }
    }

    public void RemoveStopped(string runId)
    {
        lock (_gate)
        {
            _stopped.Remove(runId);
        }
    }

    public void ClearStops()
    {
        lock (_gate)
        {
            _stopped.Clear();
            _steps.Clear();
            _pauseRequested = false;
        }
    }

    private sealed record StepPlan(PluginDebugStepKind Kind, int StartingDepth)
    {
        public bool ShouldStop(SandboxDebugCheckpoint checkpoint)
            => Kind switch
            {
                PluginDebugStepKind.StepIn => true,
                PluginDebugStepKind.StepOver => checkpoint.Frame.Depth < StartingDepth ||
                    (checkpoint.Frame.Depth == StartingDepth && IsSourceBoundary(checkpoint.Kind)),
                PluginDebugStepKind.StepOut => checkpoint.Frame.Depth < StartingDepth,
                _ => false
            };

        private static bool IsSourceBoundary(SandboxDebugCheckpointKind kind)
            => kind is SandboxDebugCheckpointKind.Statement or
                SandboxDebugCheckpointKind.LoopIteration or
                SandboxDebugCheckpointKind.FunctionExit;
    }
}

internal enum PluginDebugStepKind
{
    Continue,
    StepIn,
    StepOver,
    StepOut
}

internal sealed record PluginDebugStoppedExecution(
    string PluginId,
    SandboxDebugCheckpoint Checkpoint,
    string Reason);

internal sealed record PluginDebugBreakpointSpec(
    SandboxNodeId NodeId,
    string? Condition = null,
    int? HitCount = null,
    string? LogMessage = null);

internal sealed class PluginDebugBreakpointState(PluginDebugBreakpointSpec spec)
{
    public PluginDebugBreakpointSpec Spec { get; } = spec;

    public long Hits { get; set; }
}

internal sealed record PluginDebugCheckpointDecision(
    bool ShouldStop,
    string? Reason,
    PluginDebugBreakpointSpec? Breakpoint)
{
    public static PluginDebugCheckpointDecision None { get; } = new(false, null, null);

    public static PluginDebugCheckpointDecision Stop(string reason) => new(true, reason, null);

    public static PluginDebugCheckpointDecision ForBreakpoint(PluginDebugBreakpointSpec breakpoint)
        => new(false, null, breakpoint);
}
