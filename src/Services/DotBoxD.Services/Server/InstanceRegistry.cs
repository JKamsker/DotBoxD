using System.Collections.Concurrent;
using DotBoxD.Services.Diagnostics;

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
        ThrowIfInvalidKey(serviceName, nameof(serviceName), "Service name");

        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        lock (_gate)
        {
            if (_closed)
            {
                throw new InvalidOperationException("Instance registry is closed.");
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
        if (IsInvalidKey(serviceName) || IsInvalidKey(instanceId))
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
    public void Release(string serviceName, string instanceId)
    {
        ValidateKeys(serviceName, instanceId);

        object? instance = null;
        lock (_gate)
        {
            if (_entries.TryRemove((serviceName, instanceId), out var removed))
            {
                _count--;
                instance = removed;
            }
        }

        if (instance is not null)
        {
            DisposeInstance(instance);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(string serviceName, string instanceId)
    {
        ValidateKeys(serviceName, instanceId);

        object? instance = null;
        lock (_gate)
        {
            if (_entries.TryRemove((serviceName, instanceId), out var removed))
            {
                _count--;
                instance = removed;
            }
        }

        if (instance is not null)
        {
            await DisposeInstanceAsync(instance).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void ReleaseAll()
    {
        foreach (var instance in DrainAll())
        {
            DisposeInstance(instance);
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
            await DisposeInstanceAsync(instance).ConfigureAwait(false);
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
                instances.Add(instance);
            }

            _entries.Clear();
            _count = 0;
            return instances;
        }
    }

    private static void ValidateKeys(string serviceName, string instanceId)
    {
        ThrowIfInvalidKey(serviceName, nameof(serviceName), "Service name");
        ThrowIfInvalidKey(instanceId, nameof(instanceId), "Instance id");
    }

    private static void ThrowIfInvalidKey(string value, string paramName, string label)
    {
        if (IsInvalidKey(value))
        {
            throw new ArgumentException(label + " must not be null, empty, or whitespace.", paramName);
        }
    }

    private static bool IsInvalidKey(string? value) => string.IsNullOrWhiteSpace(value);

    // Sub-service instances are connection-scoped and owned by the registry (see IInstanceRegistry),
    // so they are disposed when released. Best-effort: a faulting dispose is reported via diagnostics
    // but never breaks teardown. IAsyncDisposable is preferred when present; run it away from the
    // caller's SynchronizationContext before blocking so user disposers that capture context can finish.
    private static void DisposeInstance(object instance)
    {
        try
        {
            switch (instance)
            {
                case IAsyncDisposable asyncDisposable:
                    DisposeAsyncSynchronously(asyncDisposable);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Sub-service instance disposal failed", ex);
        }
    }

    private static void DisposeAsyncSynchronously(IAsyncDisposable asyncDisposable)
    {
        Task.Run(async () => await asyncDisposable.DisposeAsync().ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();
    }

    // Async counterpart of DisposeInstance for the teardown drain: awaits IAsyncDisposable.DisposeAsync
    // (preferred when present) instead of blocking on it, so a suspending user disposer never sync-blocks
    // a pooled thread. Same best-effort contract: a faulting dispose is reported, never rethrown.
    private static async Task DisposeInstanceAsync(object instance)
    {
        try
        {
            switch (instance)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Sub-service instance disposal failed", ex);
        }
    }
}
