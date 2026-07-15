using System.Text.Json;

namespace DotBoxD.DebugAdapter;

internal sealed class DapInspectionDispatcher
{
    private readonly HashSet<Task> _tasks = [];
    private readonly object _gate = new();

    public void Dispatch(
        JsonElement request,
        CancellationToken cancellationToken,
        Func<JsonElement, CancellationToken, ValueTask> handler)
    {
        var task = handler(request.Clone(), cancellationToken).AsTask();
        lock (_gate)
        {
            _tasks.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_gate)
                {
                    _tasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async Task CompleteAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            tasks = _tasks.ToArray();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
