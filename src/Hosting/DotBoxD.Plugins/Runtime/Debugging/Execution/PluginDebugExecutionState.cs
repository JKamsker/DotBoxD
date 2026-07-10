using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugExecutionState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<SandboxNodeId>> _breakpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SandboxDebugCheckpoint> _stopped = new(StringComparer.Ordinal);
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
                reason = "pause";
                return true;
            }

            if (checkpoint.Kind == SandboxDebugCheckpointKind.Exception)
            {
                reason = "exception";
                return true;
            }

            if (_breakpoints.TryGetValue(pluginId, out var nodes) && nodes.Contains(checkpoint.Node.Id))
            {
                reason = "breakpoint";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    public void RecordStopped(SandboxDebugCheckpoint checkpoint)
    {
        lock (_gate)
        {
            _stopped[checkpoint.RunId.ToString()] = checkpoint;
        }
    }

    public bool ContainsStopped(string runId)
    {
        lock (_gate)
        {
            return _stopped.ContainsKey(runId);
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
            _pauseRequested = false;
        }
    }
}
