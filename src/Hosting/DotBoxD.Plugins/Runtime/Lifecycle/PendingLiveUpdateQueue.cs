namespace DotBoxD.Plugins.Runtime.Lifecycle;

internal sealed class PendingLiveUpdateQueue
{
    private readonly object _gate = new();
    private readonly List<Task> _pending = [];

    public Exception? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    private Exception? _lastError;
    private long _errorVersion;

    public void Enqueue(Action update)
    {
        Task task;
        lock (_gate)
        {
            task = Task.Run(() =>
            {
                try
                {
                    update();
                }
                catch (Exception ex)
                {
                    lock (_gate)
                    {
                        _lastError = ex;
                        _errorVersion++;
                    }

                    throw;
                }
            });
            _pending.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                if (!completed.IsCompletedSuccessfully)
                {
                    return;
                }

                lock (_gate)
                {
                    _pending.Remove(completed);
                }
            },
            TaskScheduler.Default);
    }

    public void ClearError()
    {
        lock (_gate)
        {
            _lastError = null;
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task[] pending;
        long errorVersion;
        lock (_gate)
        {
            _pending.RemoveAll(task => task.IsCompletedSuccessfully);
            pending = _pending.ToArray();
            errorVersion = _errorVersion;
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var observed = new HashSet<Task>(pending);
            lock (_gate)
            {
                // Later enqueues were not awaited by this flush and must keep their failures.
                _pending.RemoveAll(observed.Contains);
                _lastError ??= ex;
            }

            throw new InvalidOperationException("A fire-and-forget live setting update failed.", ex);
        }

        lock (_gate)
        {
            _pending.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_errorVersion == errorVersion)
            {
                _lastError = null;
            }
        }
    }
}
