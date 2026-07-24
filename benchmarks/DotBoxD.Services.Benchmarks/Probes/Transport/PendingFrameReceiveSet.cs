using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

/// <summary>Retains pending frame receives in direct or task-backed form for cold-footprint probes.</summary>
internal sealed class PendingFrameReceiveSet
{
    private readonly List<ValueTask<RpcFrame>>? _directReceives;
    private readonly List<Task<RpcFrame>>? _taskReceives;

    public PendingFrameReceiveSet(int capacity, bool taskBacked)
    {
        if (taskBacked)
        {
            _taskReceives = new List<Task<RpcFrame>>(capacity);
        }
        else
        {
            _directReceives = new List<ValueTask<RpcFrame>>(capacity);
        }
    }

    public int Count => _taskReceives?.Count ?? _directReceives!.Count;

    public void Add(ValueTask<RpcFrame> receive)
    {
        if (_taskReceives is not null)
        {
            var task = receive.AsTask();
            EnsurePending(task.IsCompleted);
            _taskReceives.Add(task);
            return;
        }

        EnsurePending(receive.IsCompleted);
        _directReceives!.Add(receive);
    }

    public async Task ObserveAsync()
    {
        if (_taskReceives is not null)
        {
            foreach (var receive in _taskReceives)
            {
                await ObserveAsync(new ValueTask<RpcFrame>(receive)).ConfigureAwait(false);
            }

            return;
        }

        foreach (var receive in _directReceives!)
        {
            await ObserveAsync(receive).ConfigureAwait(false);
        }
    }

    private static void EnsurePending(bool isCompleted)
    {
        if (isCompleted)
        {
            throw new InvalidOperationException(
                "Idle receive completed before peer bytes were written.");
        }
    }

    private static async ValueTask ObserveAsync(ValueTask<RpcFrame> receive)
    {
        try
        {
            using var frame = await receive.ConfigureAwait(false);
        }
        catch
        {
            // Connection disposal is expected to end every pending receive in this probe.
        }
    }
}
