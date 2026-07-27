using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Hosting.Execution.Compiled;

internal sealed class CompiledArtifactExecutionCache
{
    private const int Capacity = 64;

    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recency = new();
    private readonly object _gate = new();

    public async ValueTask<CompiledArtifact> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        Func<CancellationToken, ValueTask<CompiledArtifact>> compile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CacheKey(plan.PlanHash, entrypoint);
        CacheLookup lookup;
        lock (_gate)
        {
            lookup = TouchOrAdd(key, compile);
        }

        return await CompleteAsync(key, lookup, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CompiledArtifact> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        ISandboxCompiler compiler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CacheKey(plan.PlanHash, entrypoint);
        CacheLookup lookup;
        lock (_gate)
        {
            lookup = TouchOrAdd(key, compiler, plan, entrypoint);
        }

        return await CompleteAsync(key, lookup, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<CompiledArtifact> CompleteAsync(
        CacheKey key,
        CacheLookup lookup,
        CancellationToken cancellationToken)
        => lookup.Completed is { } completed
            ? ValueTask.FromResult(completed)
            : AwaitAndMarkAsync(key, lookup.Lazy!, cancellationToken);

    private async ValueTask<CompiledArtifact> AwaitAndMarkAsync(
        CacheKey key,
        Lazy<Task<CompiledArtifact>> lazy,
        CancellationToken cancellationToken)
    {
        try
        {
            var artifact = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            MarkCompleted(key, lazy, artifact);
            return artifact;
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
        Func<CancellationToken, ValueTask<CompiledArtifact>> compile)
    {
        return TryTouchExisting(key, out var existing)
            ? existing
            : AddEntry(key, compile);
    }

    private CacheLookup TouchOrAdd(
        CacheKey key,
        ISandboxCompiler compiler,
        ExecutionPlan plan,
        string entrypoint)
    {
        return TryTouchExisting(key, out var existing)
            ? existing
            : AddEntry(key, compiler, plan, entrypoint);
    }

    private CacheLookup AddEntry(
        CacheKey key,
        Func<CancellationToken, ValueTask<CompiledArtifact>> compile)
    {
        var candidate = new Lazy<Task<CompiledArtifact>>(
            () => compile(CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return AddCandidate(key, candidate);
    }

    private CacheLookup AddEntry(
        CacheKey key,
        ISandboxCompiler compiler,
        ExecutionPlan plan,
        string entrypoint)
    {
        var candidate = new Lazy<Task<CompiledArtifact>>(
            () => compiler.CompileAsync(
                plan,
                new CompileOptions(entrypoint),
                CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return AddCandidate(key, candidate);
    }

    private CacheLookup AddCandidate(
        CacheKey key,
        Lazy<Task<CompiledArtifact>> candidate)
    {
        var node = _recency.AddLast(new Entry(key, candidate));
        _entries[key] = node;
        if (_entries.Count > Capacity)
        {
            var oldest = _recency.First!;
            _recency.Remove(oldest);
            _entries.Remove(oldest.Value.Key);
        }

        return new CacheLookup(null, candidate);
    }

    private bool TryTouchExisting(CacheKey key, out CacheLookup lookup)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            Touch(existing);
            lookup = existing.Value.Completed is null
                ? new CacheLookup(null, existing.Value.Artifact)
                : new CacheLookup(existing.Value.Completed, null);
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

    private void MarkCompleted(CacheKey key, Lazy<Task<CompiledArtifact>> lazy, CompiledArtifact artifact)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Artifact, lazy))
            {
                current.Value.Completed = artifact;
            }
        }
    }

    private void RemoveIfCurrent(CacheKey key, Lazy<Task<CompiledArtifact>> lazy)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Artifact, lazy))
            {
                _recency.Remove(current);
                _entries.Remove(key);
            }
        }
    }

    private readonly record struct CacheLookup(CompiledArtifact? Completed, Lazy<Task<CompiledArtifact>>? Lazy);

    private readonly record struct CacheKey(string PlanHash, string Entrypoint);

    private sealed class Entry(CacheKey key, Lazy<Task<CompiledArtifact>> artifact)
    {
        public CacheKey Key { get; } = key;
        public Lazy<Task<CompiledArtifact>> Artifact { get; } = artifact;
        public CompiledArtifact? Completed { get; set; }
    }
}
