using System.Threading.Channels;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia;

internal sealed class UiInputQueue
{
    private readonly Channel<UiInput> _queue;
    // Written, checked, and acknowledged only on the UI thread; reads use the channel.
    private readonly Dictionary<(int Node, UiPropertyId Property), UiInput> _latest = [];

    public UiInputQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _queue = Channel.CreateBounded<UiInput>(new BoundedChannelOptions(capacity)
        { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public ValueTask<UiInput> ReadAsync(CancellationToken token) => _queue.Reader.ReadAsync(token);
    public void Write(UiInput input)
    {
        if (!_queue.Writer.TryWrite(input))
        { _queue.Writer.TryComplete(new UiValidationException("Avalonia UI input queue limit exceeded.")); }
        else if (input.NodeId > 0 && input.Value is not null)
        { _latest[(input.NodeId, input.PropertyId)] = input; }
    }
    public bool HasPending(int nodeId, UiPropertyId propertyId) => _latest.ContainsKey((nodeId, propertyId));
    public bool Acknowledge(UiInput input)
    {
        var key = (input.NodeId, input.PropertyId);
        return _latest.TryGetValue(key, out var latest) && ReferenceEquals(latest, input) && _latest.Remove(key);
    }
    public void Complete()
    {
        _queue.Writer.TryComplete();
        _latest.Clear();
        while (_queue.Reader.TryRead(out _))
        { }
    }
}
