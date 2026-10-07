using System.Threading.Channels;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia;

internal sealed class UiInputQueue
{
    private readonly Channel<UiInput> _queue;

    public UiInputQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _queue = Channel.CreateBounded<UiInput>(new BoundedChannelOptions(capacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public ValueTask<UiInput> ReadAsync(CancellationToken token) => _queue.Reader.ReadAsync(token);
    public void Write(UiInput input)
    {
        if (!_queue.Writer.TryWrite(input))
        { _queue.Writer.TryComplete(new UiValidationException("Avalonia UI input queue limit exceeded.")); }
    }
    public void Complete() => _queue.Writer.TryComplete();
}
