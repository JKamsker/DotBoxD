using ShaRPC.Core.Buffers;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcCanceledInboundStreams
{
    internal const int Capacity = 1024;

    private readonly object _gate = new();
    private readonly Queue<(int StreamId, long Version)> _order = new();
    private readonly Dictionary<int, long> _streamIds = new();
    private long _version;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _streamIds.Count;
            }
        }
    }

    public void Add(int streamId)
    {
        lock (_gate)
        {
            if (_streamIds.ContainsKey(streamId))
            {
                return;
            }

            var version = ++_version;
            _streamIds.Add(streamId, version);
            _order.Enqueue((streamId, version));
            Trim();
        }
    }

    public bool TryConsumeItem(int streamId, Payload frame)
    {
        if (!Contains(streamId))
        {
            return false;
        }

        frame.Dispose();
        return true;
    }

    public bool TryRemove(int streamId)
    {
        lock (_gate)
        {
            return _streamIds.Remove(streamId);
        }
    }

    public void Remove(int streamId)
    {
        lock (_gate)
        {
            _streamIds.Remove(streamId);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _streamIds.Clear();
            _order.Clear();
        }
    }

    private bool Contains(int streamId)
    {
        lock (_gate)
        {
            return _streamIds.ContainsKey(streamId);
        }
    }

    private void Trim()
    {
        while (_streamIds.Count > Capacity && _order.Count > 0)
        {
            var entry = _order.Dequeue();
            if (_streamIds.TryGetValue(entry.StreamId, out var version) &&
                version == entry.Version)
            {
                _streamIds.Remove(entry.StreamId);
            }
        }
    }
}
