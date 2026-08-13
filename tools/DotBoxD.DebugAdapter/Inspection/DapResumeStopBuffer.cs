namespace DotBoxD.DebugAdapter;

internal sealed class DapResumeStopBuffer
{
    private readonly object _gate = new();
    private readonly Queue<DapPendingStop> _pendingStops = new();
    private bool _resuming;

    public void BeginResume()
    {
        lock (_gate)
        {
            _resuming = true;
        }
    }

    public bool TryBuffer(DapPendingStop stop)
    {
        lock (_gate)
        {
            if (!_resuming)
            {
                return false;
            }

            _pendingStops.Enqueue(stop);
            return true;
        }
    }

    public DapPendingStop[] CompleteResume()
    {
        lock (_gate)
        {
            _resuming = false;
            var pending = _pendingStops.ToArray();
            _pendingStops.Clear();
            return pending;
        }
    }
}

internal sealed record DapPendingStop(string RunId, string PluginId, string? Reason);
