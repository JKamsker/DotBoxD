namespace DotBoxD.Hosting;

using DotBoxD.Kernels;
using DotBoxD.Kernels.Compiler;

internal sealed class CompiledArtifactExecutionCache
{
    private const int Capacity = 64;

    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recency = new();
    private readonly object _gate = new();

    public async ValueTask<CompiledArtifact> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        Func<CancellationToken, ValueTask<CompiledArtifact>> compile,
        CancellationToken cancellationToken)
    {
        var key = Key(plan, entrypoint);
        CacheLookup lookup;
        lock (_gate)
        {
            lookup = TouchOrAdd(key, compile);
        }

        if (lookup.Completed is not null)
        {
            return lookup.Completed;
        }

        var lazy = lookup.Lazy!;
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
        string key,
        Func<CancellationToken, ValueTask<CompiledArtifact>> compile)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            _recency.Remove(existing);
            _recency.AddLast(existing);
            return existing.Value.Completed is null
                ? new CacheLookup(null, existing.Value.Artifact)
                : new CacheLookup(existing.Value.Completed, null);
        }

        var candidate = new Lazy<Task<CompiledArtifact>>(
            () => compile(CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
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

    private void MarkCompleted(string key, Lazy<Task<CompiledArtifact>> lazy, CompiledArtifact artifact)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Artifact, lazy))
            {
                current.Value.Completed = artifact;
            }
        }
    }

    private void RemoveIfCurrent(string key, Lazy<Task<CompiledArtifact>> lazy)
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

    private static string Key(ExecutionPlan plan, string entrypoint)
        => plan.PlanHash + "|" + entrypoint;

    private readonly record struct CacheLookup(CompiledArtifact? Completed, Lazy<Task<CompiledArtifact>>? Lazy);

    private sealed class Entry(string key, Lazy<Task<CompiledArtifact>> artifact)
    {
        public string Key { get; } = key;
        public Lazy<Task<CompiledArtifact>> Artifact { get; } = artifact;
        public CompiledArtifact? Completed { get; set; }
    }
}
