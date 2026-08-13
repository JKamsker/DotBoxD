namespace DotBoxD.Pushdown.Services;

internal sealed class PluginDebugSourceRefreshTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<long, TaskCompletionSource> _waiters = [];
    private long _registeredVersion;
    private long _acknowledgedVersion;

    public long Register()
    {
        lock (_gate)
        {
            return ++_registeredVersion;
        }
    }

    public void Acknowledge(long version)
    {
        TaskCompletionSource[] completed;
        lock (_gate)
        {
            if (version <= 0 || version > _registeredVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(version),
                    version,
                    "Source refresh acknowledgements must identify a registered version.");
            }

            if (version <= _acknowledgedVersion)
            {
                return;
            }

            _acknowledgedVersion = version;
            completed = _waiters
                .Where(waiter => waiter.Key <= version)
                .Select(waiter => waiter.Value)
                .ToArray();
            foreach (var waiter in _waiters.Keys.Where(candidate => candidate <= version).ToArray())
            {
                _waiters.Remove(waiter);
            }
        }

        foreach (var completion in completed)
        {
            completion.TrySetResult();
        }
    }

    public async ValueTask WaitAsync(long version, TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (version <= _acknowledgedVersion)
            {
                return;
            }

            if (!_waiters.TryGetValue(version, out var existing))
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(version, completion);
            }
            else
            {
                completion = existing;
            }
        }

        try
        {
            await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (_waiters.TryGetValue(version, out var current) && ReferenceEquals(current, completion))
                {
                    _waiters.Remove(version);
                }
            }
        }
    }

    public async ValueTask WaitForConfigurationAsync(
        Task configured,
        long version,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await configured.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        await WaitAsync(version, timeout, cancellationToken).ConfigureAwait(false);
    }
}
