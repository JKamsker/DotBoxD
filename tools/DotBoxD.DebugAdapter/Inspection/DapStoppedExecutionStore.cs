namespace DotBoxD.DebugAdapter;

internal sealed class DapStoppedExecutionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, string> _threads = [];
    private readonly Dictionary<int, string> _threadPlugins = [];
    private readonly Dictionary<string, int> _threadIds = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _frames = [];
    private readonly Dictionary<int, DapFrameContext> _frameContexts = [];
    private readonly Dictionary<int, int> _frameThreads = [];
    private int _nextThreadId;
    private int _nextFrameId;

    public int RecordThread(string runId, string? pluginId = null)
    {
        lock (_gate)
        {
            if (!_threadIds.TryGetValue(runId, out var threadId))
            {
                threadId = ++_nextThreadId;
                _threadIds[runId] = threadId;
            }

            _threads[threadId] = runId;
            if (pluginId is not null)
            {
                _threadPlugins[threadId] = pluginId;
            }

            return threadId;
        }
    }

    public string RunId(int threadId)
    {
        lock (_gate)
        {
            return _threads.TryGetValue(threadId, out var runId)
                ? runId
                : throw new DebugAdapterException("staleThread", "The selected kernel execution is no longer stopped.");
        }
    }

    public string PluginId(int threadId, string fallback)
    {
        lock (_gate)
        {
            return _threadPlugins.GetValueOrDefault(threadId, fallback);
        }
    }

    public int AddFrame(
        int threadId,
        string remoteFrameId,
        string pluginId,
        string functionId,
        IReadOnlyList<DapSourceVariableBinding> bindings)
    {
        lock (_gate)
        {
            var frameId = ++_nextFrameId;
            _frames[frameId] = remoteFrameId;
            _frameContexts[frameId] = new DapFrameContext(remoteFrameId, pluginId, functionId, bindings);
            _frameThreads[frameId] = threadId;
            return frameId;
        }
    }

    public string Frame(int frameId)
    {
        lock (_gate)
        {
            return _frames.TryGetValue(frameId, out var frame)
                ? frame
                : throw new DebugAdapterException("staleFrame", "The selected stack frame is no longer stopped.");
        }
    }

    public DapFrameContext FrameContext(int frameId)
    {
        lock (_gate)
        {
            return _frameContexts.TryGetValue(frameId, out var context)
                ? context
                : throw new DebugAdapterException("staleFrame", "The selected stack frame is no longer stopped.");
        }
    }

    public int DapFrameId(string remoteFrameId)
    {
        lock (_gate)
        {
            foreach (var frame in _frames)
            {
                if (string.Equals(frame.Value, remoteFrameId, StringComparison.Ordinal))
                {
                    return frame.Key;
                }
            }

            throw new DebugAdapterException("staleFrame", "The selected stack frame is no longer stopped.");
        }
    }

    public IReadOnlyList<DapFrameContext> FrameContexts(int threadId)
    {
        lock (_gate)
        {
            return _frameThreads
                .Where(frame => frame.Value == threadId)
                .Select(frame => _frameContexts[frame.Key])
                .ToArray();
        }
    }

    public IReadOnlySet<string> RemoveThread(int threadId, bool preserveIdentity = false)
    {
        lock (_gate)
        {
            if (_threads.Remove(threadId, out var runId) && !preserveIdentity)
            {
                _threadIds.Remove(runId);
            }

            _threadPlugins.Remove(threadId);
            var frameIds = _frameThreads
                .Where(item => item.Value == threadId)
                .Select(item => item.Key)
                .ToArray();
            var remoteFrames = frameIds.Select(frameId => _frames[frameId]).ToHashSet(StringComparer.Ordinal);
            foreach (var frameId in frameIds)
            {
                _frames.Remove(frameId);
                _frameContexts.Remove(frameId);
                _frameThreads.Remove(frameId);
            }

            return remoteFrames;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _threads.Clear();
            _threadPlugins.Clear();
            _threadIds.Clear();
            _frames.Clear();
            _frameContexts.Clear();
            _frameThreads.Clear();
        }
    }
}

internal sealed record DapFrameContext(
    string RemoteFrameId,
    string PluginId,
    string FunctionId,
    IReadOnlyList<DapSourceVariableBinding> Bindings);
