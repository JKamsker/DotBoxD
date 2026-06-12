namespace SafeIR.Plugins;

internal sealed class PendingLiveUpdateQueue
{
    private readonly object _gate = new();
    private readonly List<Task> _pending = [];

    public Exception? LastError
    {
        get
        {
            lock (_gate) {
                return _lastError;
            }
        }
    }

    private Exception? _lastError;

    public void Enqueue(Action update)
    {
        var task = Task.Run(() => {
            try {
                update();
                lock (_gate) {
                    _lastError = null;
                }
            }
            catch (Exception ex) {
                lock (_gate) {
                    _lastError = ex;
                }

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

    public void ClearError()
    {
        lock (_gate) {
            _lastError = null;
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Task[] pending;
        lock (_gate) {
            _pending.RemoveAll(task => task.IsCompleted);
            pending = _pending.ToArray();
        }

        try {
            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (LastError is not null) {
            throw new InvalidOperationException("A fire-and-forget live setting update failed.", LastError);
        }

        var lastError = LastError;
        if (lastError is not null) {
            throw new InvalidOperationException("A fire-and-forget live setting update failed.", lastError);
        }
    }
}
