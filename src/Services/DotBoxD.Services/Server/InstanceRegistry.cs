using System.Collections.Concurrent;

namespace DotBoxD.Services.Server;

/// <summary>
/// Default <see cref="IInstanceRegistry"/>. Backed by a single
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed on
/// <c>(serviceName, instanceId)</c>. One registry per connection.
/// </summary>
public sealed class InstanceRegistry : IInstanceRegistry
{
    internal const int DefaultMaxInstances = 1024;

    private readonly ConcurrentDictionary<(string Service, string Id), object> _entries = new();
    private readonly List<object> _activeInstances = [];
    private readonly List<object> _disposing = [];
    private readonly List<InstanceRegistryDisposal> _pendingDisposals = [];
    private readonly object _gate = new();
    private readonly int _maxInstances;
    private int _count;
    private bool _closed;

    public InstanceRegistry() : this(DefaultMaxInstances) { }

    public InstanceRegistry(int maxInstances)
    {
        if (maxInstances <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInstances),
                maxInstances,
                "Maximum instances must be greater than zero.");
        }

        _maxInstances = maxInstances;
    }

    /// <inheritdoc />
    public string Register(string serviceName, object instance)
    {
        InstanceRegistryPolicy.ThrowIfInvalidKey(serviceName, nameof(serviceName), "Service name");

        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        lock (_gate)
        {
            if (_closed)
            {
                throw new InvalidOperationException("Instance registry is closed.");
            }

            if (InstanceRegistryPolicy.ContainsReference(_disposing, instance) ||
                InstanceRegistryPolicy.ContainsPendingDisposal(_pendingDisposals, instance))
            {
                throw new InvalidOperationException("Cannot register an instance while it is being disposed.");
            }

            if (_count >= _maxInstances)
            {
                throw new InvalidOperationException(
                    $"Instance registry limit reached ({_maxInstances}). Release unused instances before registering new ones.");
            }

            var id = Guid.NewGuid().ToString("N");
            _entries[(serviceName, id)] = instance;
            _count++;
            return id;
        }
    }

    /// <inheritdoc />
    public bool TryGet(string serviceName, string instanceId, out object instance)
    {
        if (InstanceRegistryPolicy.IsInvalidKey(serviceName) ||
            InstanceRegistryPolicy.IsInvalidKey(instanceId))
        {
            instance = null!;
            return false;
        }

        if (_entries.TryGetValue((serviceName, instanceId), out var value))
        {
            instance = value;
            return true;
        }
        instance = null!;
        return false;
    }

    /// <inheritdoc />
    public bool TryAcquire(string serviceName, string instanceId, out object instance, out IAsyncDisposable lease)
    {
        lock (_gate)
        {
            if (InstanceRegistryPolicy.IsInvalidKey(serviceName) ||
                InstanceRegistryPolicy.IsInvalidKey(instanceId) ||
                !_entries.TryGetValue((serviceName, instanceId), out instance!))
            {
                instance = null!;
                lease = null!;
                return false;
            }

            _activeInstances.Add(instance);
            lease = new InstanceRegistryLease(this, instance);
            return true;
        }
    }

    /// <inheritdoc />
    public void Release(string serviceName, string instanceId)
    {
        InstanceRegistryPolicy.ValidateKeys(serviceName, instanceId);

        var disposal = RemoveForDisposal(serviceName, instanceId);

        if (disposal is not null)
        {
            if (disposal.IsReady)
            {
                DisposeAndComplete(disposal);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(string serviceName, string instanceId)
    {
        InstanceRegistryPolicy.ValidateKeys(serviceName, instanceId);

        var disposal = RemoveForDisposal(serviceName, instanceId);

        if (disposal is not null)
        {
            if (disposal.IsReady)
            {
                await DisposeAndCompleteAsync(disposal).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public void ReleaseAll()
    {
        foreach (var instance in DrainAll())
        {
            InstanceRegistryDisposer.DisposeBestEffort(instance);
        }
    }

    /// <summary>
    /// Async teardown drain used by the connection-cleanup path. Like <see cref="ReleaseAll"/> it removes
    /// and disposes every registered instance, but it awaits <see cref="IAsyncDisposable.DisposeAsync"/>
    /// rather than blocking a pooled thread on it — avoiding the thread-pool starvation / captured-context
    /// deadlock that sync-over-async disposal causes when a user disposer suspends.
    /// </summary>
    internal async Task ReleaseAllAsync()
    {
        foreach (var instance in DrainAll())
        {
            await InstanceRegistryDisposer.DisposeAsyncBestEffort(instance).ConfigureAwait(false);
        }
    }

    private List<object> DrainAll()
    {
        lock (_gate)
        {
            _closed = true;
            var instances = new List<object>(_entries.Count);
            foreach (var instance in _entries.Values)
            {
                if (!InstanceRegistryPolicy.ContainsReference(instances, instance))
                {
                    instances.Add(instance);
                }
            }

            _entries.Clear();
            _count = 0;
            return instances;
        }
    }

    internal async ValueTask ReleaseLeaseAsync(object instance)
    {
        InstanceRegistryDisposal? disposal = null;
        lock (_gate)
        {
            InstanceRegistryPolicy.RemoveReference(_activeInstances, instance);
            if (!InstanceRegistryPolicy.ContainsReference(_activeInstances, instance) &&
                !InstanceRegistryPolicy.ContainsReference(_entries.Values, instance))
            {
                disposal = TakePendingDisposal(instance);
            }
        }

        if (disposal is not null)
        {
            await DisposeAndCompleteAsync(disposal).ConfigureAwait(false);
        }
    }

    private InstanceRegistryDisposal? RemoveForDisposal(string serviceName, string instanceId)
    {
        lock (_gate)
        {
            if (!_entries.TryRemove((serviceName, instanceId), out var instance))
            {
                return null;
            }

            _count--;
            if (InstanceRegistryPolicy.ContainsReference(_entries.Values, instance))
            {
                return null;
            }

            var disposal = new InstanceRegistryDisposal(instance);
            if (InstanceRegistryPolicy.ContainsReference(_activeInstances, instance))
            {
                _pendingDisposals.Add(disposal);
                return disposal;
            }

            _disposing.Add(instance);
            disposal.MarkReady();
            return disposal;
        }
    }

    private InstanceRegistryDisposal? TakePendingDisposal(object instance)
    {
        for (var index = 0; index < _pendingDisposals.Count; index++)
        {
            var disposal = _pendingDisposals[index];
            if (!ReferenceEquals(disposal.Instance, instance))
            {
                continue;
            }

            _pendingDisposals.RemoveAt(index);
            _disposing.Add(instance);
            disposal.MarkReady();
            return disposal;
        }

        return null;
    }

    private void CompleteDisposal(object instance)
    {
        lock (_gate)
        {
            InstanceRegistryPolicy.RemoveReference(_disposing, instance);
        }
    }

    private void DisposeAndComplete(InstanceRegistryDisposal disposal)
    {
        try
        {
            InstanceRegistryDisposer.Dispose(disposal.Instance);
            disposal.Completion.SetResult(true);
        }
        catch (Exception ex)
        {
            disposal.Completion.SetException(ex);
            throw;
        }
        finally
        {
            CompleteDisposal(disposal.Instance);
        }
    }

    private async Task DisposeAndCompleteAsync(InstanceRegistryDisposal disposal)
    {
        try
        {
            await InstanceRegistryDisposer.DisposeAsync(disposal.Instance).ConfigureAwait(false);
            disposal.Completion.SetResult(true);
        }
        catch (Exception ex)
        {
            disposal.Completion.SetException(ex);
            throw;
        }
        finally
        {
            CompleteDisposal(disposal.Instance);
        }
    }

}
