using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Hosting.Execution.Compiled;

internal sealed class CompiledExecutableExecutionCache : IDisposable
{
    private const int Capacity = 64;

    private readonly Dictionary<CompiledExecutableExecutionKey, LinkedListNode<CompiledExecutableExecutionEntry>> _entries = new();
    private readonly LinkedList<CompiledExecutableExecutionEntry> _recency = new();
    private readonly CompiledExecutableCacheSynchronization _gate = new();

    public async ValueTask<CompiledExecutable> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CompiledExecutableExecutionKey(plan.PlanHash, entrypoint);
        CacheLookup lookup;
        lock (_gate)
        {
            lookup = TouchOrAdd(key, plan, materialize);
        }

        return await CompleteAsync(key, lookup, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CompiledExecutable> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        CompiledExecutableCache materialized,
        CompiledArtifact artifact,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CompiledExecutableExecutionKey(plan.PlanHash, entrypoint);
        CacheLookup lookup;
        lock (_gate)
        {
            lookup = TouchOrAdd(key, materialized, artifact, plan, entrypoint);
        }

        return await CompleteAsync(key, lookup, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_gate.TryBeginDispose(out var hotEntry))
            {
                return;
            }

            hotEntry?.Dispose();
            foreach (var entry in _recency)
            {
                entry.Invalidate();
            }

            _entries.Clear();
            _recency.Clear();
        }
    }

    internal bool TryPublishMostRecentCompletedExact(
        ExecutionPlan plan,
        string entrypoint,
        CompiledExecutableHotEntry hotEntry)
    {
        lock (_gate)
        {
            if (_recency.Last is { Value: var mostRecent } &&
                mostRecent.Completed is not null &&
                mostRecent.Matches(plan, entrypoint))
            {
                return hotEntry.TryPublish(mostRecent.GetOrCreatePublication());
            }

            return false;
        }
    }

    internal bool TryGetCompletedExactWithoutTouch(
        ExecutionPlan plan,
        string entrypoint,
        out CompiledExecutable executable)
    {
        var key = new CompiledExecutableExecutionKey(plan.PlanHash, entrypoint);
        lock (_gate)
        {
            // Hot execution skips both backing LRUs. Do not touch only this cache here, or the
            // artifact and executable eviction orders diverge under a larger working set.
            if (_entries.TryGetValue(key, out var current) &&
                current.Value.Completed is { } completed &&
                current.Value.Matches(plan, entrypoint))
            {
                executable = completed with { MaterializationStatus = "Hit" };
                return true;
            }

            executable = default;
            return false;
        }
    }

    internal CompiledExecutableHotEntry GetOrCreateHotEntry()
    {
        lock (_gate)
        {
            return _gate.GetOrCreateHotEntry(this);
        }
    }

    internal bool TryGetHot(
        ExecutionPlan plan,
        string entrypoint,
        out CompiledExecutable executable)
        => _gate.TryGetHot(plan, entrypoint, out executable);

    internal bool HasHot(ExecutionPlan plan, string entrypoint)
        => _gate.HasHot(plan, entrypoint);

    internal bool HasHotCapacity => _gate.HasHotCapacity;

    private ValueTask<CompiledExecutable> CompleteAsync(
        CompiledExecutableExecutionKey key,
        CacheLookup lookup,
        CancellationToken cancellationToken)
        => lookup.Completed is { } completed
            ? ValueTask.FromResult(completed with { MaterializationStatus = "Hit" })
            : AwaitAndMarkAsync(key, lookup, cancellationToken);

    private async ValueTask<CompiledExecutable> AwaitAndMarkAsync(
        CompiledExecutableExecutionKey key,
        CacheLookup lookup,
        CancellationToken cancellationToken)
    {
        var lazy = lookup.Lazy!;
        try
        {
            var executable = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            MarkCompleted(key, lazy, executable);
            return lookup.IsMiss ? executable : executable with { MaterializationStatus = "Hit" };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RemoveIfCurrent(key, lazy);
            throw;
        }
    }

    private CacheLookup TouchOrAdd(
        CompiledExecutableExecutionKey key,
        ExecutionPlan plan,
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize)
        => TryTouchExisting(key, out var existing)
            ? existing
            : AddEntry(key, plan, materialize);

    private CacheLookup TouchOrAdd(
        CompiledExecutableExecutionKey key,
        CompiledExecutableCache materialized,
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint)
        => TryTouchExisting(key, out var existing)
            ? existing
            : AddEntry(key, materialized, artifact, plan, entrypoint);

    private CacheLookup AddEntry(
        CompiledExecutableExecutionKey key,
        ExecutionPlan plan,
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize)
    {
        ThrowIfDisposed();
        var candidate = new Lazy<Task<CompiledExecutable>>(
            () => materialize(CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return AddCandidate(key, plan, candidate);
    }

    private CacheLookup AddEntry(
        CompiledExecutableExecutionKey key,
        CompiledExecutableCache materialized,
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint)
    {
        ThrowIfDisposed();
        var candidate = new Lazy<Task<CompiledExecutable>>(
            () => materialized.GetAsync(
                artifact,
                plan,
                entrypoint,
                CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return AddCandidate(key, plan, candidate);
    }

    private CacheLookup AddCandidate(
        CompiledExecutableExecutionKey key,
        ExecutionPlan plan,
        Lazy<Task<CompiledExecutable>> candidate)
    {
        var node = _recency.AddLast(new CompiledExecutableExecutionEntry(key, plan, candidate));
        _entries[key] = node;
        if (_entries.Count > Capacity)
        {
            var oldest = _recency.First!;
            Invalidate(oldest.Value);
            _recency.Remove(oldest);
            _entries.Remove(oldest.Value.Key);
        }

        return new CacheLookup(null, candidate, true);
    }

    private bool TryTouchExisting(CompiledExecutableExecutionKey key, out CacheLookup lookup)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            Touch(existing);
            lookup = existing.Value.Completed is { } completed
                ? new CacheLookup(completed, null, false)
                : new CacheLookup(null, existing.Value.Executable!, false);
            return true;
        }

        lookup = default;
        return false;
    }

    private void Touch(LinkedListNode<CompiledExecutableExecutionEntry> entry)
    {
        _recency.Remove(entry);
        _recency.AddLast(entry);
    }

    private void MarkCompleted(
        CompiledExecutableExecutionKey key,
        Lazy<Task<CompiledExecutable>> lazy,
        CompiledExecutable executable)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Executable, lazy))
            {
                current.Value.MarkCompleted(executable);
            }
        }
    }

    private void RemoveIfCurrent(
        CompiledExecutableExecutionKey key,
        Lazy<Task<CompiledExecutable>> lazy)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Executable, lazy))
            {
                Invalidate(current.Value);
                _recency.Remove(current);
                _entries.Remove(key);
            }
        }
    }

    private void ThrowIfDisposed()
        => _gate.ThrowIfDisposed(this);

    private void Invalidate(CompiledExecutableExecutionEntry entry)
    {
        _gate.Invalidate(entry);
        entry.Invalidate();
    }

    private readonly record struct CacheLookup(
        CompiledExecutable? Completed,
        Lazy<Task<CompiledExecutable>>? Lazy,
        bool IsMiss);

}
