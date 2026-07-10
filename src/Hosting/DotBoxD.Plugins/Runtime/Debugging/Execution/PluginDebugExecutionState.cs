using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugExecutionState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<SandboxNodeId>> _breakpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginDebugStoppedExecution> _stopped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StepPlan> _steps = new(StringComparer.Ordinal);
    private bool _pauseRequested;

    public IReadOnlyList<SandboxNodeId> SetBreakpoints(string pluginId, IEnumerable<string> nodeIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(nodeIds);
        var parsed = nodeIds.Select(value => new SandboxNodeId(value)).Distinct().ToArray();
        lock (_gate)
        {
            _breakpoints[pluginId] = parsed.ToHashSet();
        }

        return parsed;
    }

    public void RequestPause()
    {
        lock (_gate)
        {
            _pauseRequested = true;
        }
    }

    public bool ShouldStop(string pluginId, SandboxDebugCheckpoint checkpoint, out string reason)
    {
        lock (_gate)
        {
            if (_pauseRequested)
            {
                _pauseRequested = false;
                _steps.Remove(checkpoint.RunId.ToString());
                reason = "pause";
                return true;
            }

            if (checkpoint.Kind == SandboxDebugCheckpointKind.Exception)
            {
                _steps.Remove(checkpoint.RunId.ToString());
                reason = "exception";
                return true;
            }

            if (_breakpoints.TryGetValue(pluginId, out var nodes) && nodes.Contains(checkpoint.Node.Id))
            {
                _steps.Remove(checkpoint.RunId.ToString());
                reason = "breakpoint";
                return true;
            }

            var runId = checkpoint.RunId.ToString();
            if (_steps.TryGetValue(runId, out var step) && step.ShouldStop(checkpoint.Frame.Depth))
            {
                _steps.Remove(runId);
                reason = "step";
                return true;
            }
        }

        reason = string.Empty;
        return false;
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
            return false;
        }

        var runId = frameId[..separator];
        lock (_gate)
        {
            if (!_stopped.TryGetValue(runId, out var checkpoint))
            {
                frame = null;
                return false;
            }

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
        public bool ShouldStop(int depth)
            => Kind switch
            {
                PluginDebugStepKind.StepIn => true,
                PluginDebugStepKind.StepOver => depth <= StartingDepth,
                PluginDebugStepKind.StepOut => depth < StartingDepth,
                _ => false
            };
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
