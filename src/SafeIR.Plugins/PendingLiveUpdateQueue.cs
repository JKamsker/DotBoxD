namespace SafeIR.Plugins;

internal sealed class PendingLiveUpdateQueue
{
    private readonly object _gate = new();
    private readonly List<Task> _pending = [];

    public Exception? LastError { get; private set; }

    public void Enqueue(Action update)
    {
        var task = Task.Run(() => {
            try {
                update();
            }
            catch (Exception ex) {
                LastError = ex;
                throw;
            }
        });
        lock (_gate) {
            _pending.Add(task);
        }

        _ = task.ContinueWith(
            completed => {
                lock (_gate) {
                    _pending.Remove(completed);
                }
            },
            TaskScheduler.Default);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Task[] pending;
        lock (_gate) {
            pending = _pending.ToArray();
        }

        await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (LastError is not null) {
            throw new InvalidOperationException("A fire-and-forget live setting update failed.", LastError);
        }
    }
}
