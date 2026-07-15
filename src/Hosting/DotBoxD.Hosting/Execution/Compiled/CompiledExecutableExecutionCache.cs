using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Hosting.Execution.Compiled;

internal sealed class CompiledExecutableExecutionCache
{
    private const int Capacity = 64;

    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recency = new();
    private readonly object _gate = new();

    public async ValueTask<CompiledExecutable> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CacheKey(plan.PlanHash, entrypoint);
        CacheLookup lookup;
        lock (_gate)
        {
            lookup = TouchOrAdd(key, materialize);
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
        var key = new CacheKey(plan.PlanHash, entrypoint);
        CacheLookup lookup;
        lock (_gate)
        {
            lookup = TouchOrAdd(key, materialized, artifact, plan, entrypoint);
        }

        return await CompleteAsync(key, lookup, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<CompiledExecutable> CompleteAsync(
        CacheKey key,
        CacheLookup lookup,
        CancellationToken cancellationToken)
        => lookup.Completed is { } completed
            ? ValueTask.FromResult(completed with { MaterializationStatus = "Hit" })
            : AwaitAndMarkAsync(key, lookup, cancellationToken);

    private async ValueTask<CompiledExecutable> AwaitAndMarkAsync(
        CacheKey key,
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
        CacheKey key,
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize)
    {
        return TryTouchExisting(key, out var existing)
            ? existing
            : AddEntry(key, materialize);
    }

    private CacheLookup TouchOrAdd(
        CacheKey key,
        CompiledExecutableCache materialized,
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint)
    {
        return TryTouchExisting(key, out var existing)
            ? existing
            : AddEntry(key, materialized, artifact, plan, entrypoint);
    }

    private CacheLookup AddEntry(
        CacheKey key,
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize)
    {
        var candidate = new Lazy<Task<CompiledExecutable>>(
            () => materialize(CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return AddCandidate(key, candidate);
    }

    private CacheLookup AddEntry(
        CacheKey key,
        CompiledExecutableCache materialized,
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint)
    {
        var candidate = new Lazy<Task<CompiledExecutable>>(
            () => materialized.GetAsync(
                artifact,
                plan,
                entrypoint,
                CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return AddCandidate(key, candidate);
    }

    private CacheLookup AddCandidate(
        CacheKey key,
        Lazy<Task<CompiledExecutable>> candidate)
    {
        var node = _recency.AddLast(new Entry(key, candidate));
        _entries[key] = node;
        if (_entries.Count > Capacity)
        {
            var oldest = _recency.First!;
            _recency.Remove(oldest);
            _entries.Remove(oldest.Value.Key);
        }

        return new CacheLookup(null, candidate, true);
    }

    private bool TryTouchExisting(CacheKey key, out CacheLookup lookup)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            Touch(existing);
            lookup = existing.Value.Completed is { } completed
                ? new CacheLookup(completed, null, false)
                : new CacheLookup(null, existing.Value.Executable, false);
            return true;
        }

        lookup = default;
        return false;
    }

    private void Touch(LinkedListNode<Entry> entry)
    {
        _recency.Remove(entry);
        _recency.AddLast(entry);
    }

    private void MarkCompleted(CacheKey key, Lazy<Task<CompiledExecutable>> lazy, CompiledExecutable executable)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Executable, lazy))
            {
                current.Value.Completed = executable;
            }
        }
    }

    private void RemoveIfCurrent(CacheKey key, Lazy<Task<CompiledExecutable>> lazy)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Executable, lazy))
            {
                _recency.Remove(current);
                _entries.Remove(key);
            }
        }
    }

    private readonly record struct CacheLookup(
        CompiledExecutable? Completed,
        Lazy<Task<CompiledExecutable>>? Lazy,
        bool IsMiss);

    private readonly record struct CacheKey(string PlanHash, string Entrypoint);

    private sealed class Entry(CacheKey key, Lazy<Task<CompiledExecutable>> executable)
    {
        public CacheKey Key { get; } = key;
        public Lazy<Task<CompiledExecutable>> Executable { get; } = executable;
        public CompiledExecutable? Completed { get; set; }
    }
}
