namespace SafeIR.Hosting;

using System.Collections.Concurrent;
using SafeIR;
using SafeIR.Compiler;

internal sealed class CompiledExecutableCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<MaterializedCompiledArtifact>>> _entries =
        new(StringComparer.Ordinal);
    private int _disposed;

    public async ValueTask<CompiledExecutable> GetAsync(
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        CompiledArtifactGuard.ValidateExecutableEnvelope(artifact, plan, entrypoint);
        var key = Key(artifact);
        var candidate = new Lazy<Task<MaterializedCompiledArtifact>>(
            () => CompiledArtifactGuard.MaterializeExecutableAsync(artifact, plan, entrypoint, CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = _entries.GetOrAdd(key, candidate);
        var status = ReferenceEquals(lazy, candidate) ? "Miss" : "Hit";

        try
        {
            var materialized = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new CompiledExecutable(WithCurrentMetadata(materialized.Artifact, artifact), status);
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var lazy in _entries.Values)
        {
            DisposeWhenMaterialized(lazy);
        }

        _entries.Clear();
    }

    private static string Key(CompiledArtifact artifact)
        => artifact.Manifest.CacheKey + "|" + artifact.AssemblyHash;

    private static CompiledArtifact WithCurrentMetadata(CompiledArtifact materialized, CompiledArtifact current)
        => materialized with
        {
            CacheStatus = current.CacheStatus,
            CacheInvalidReason = current.CacheInvalidReason
        };

    private void RemoveIfCurrent(string key, Lazy<Task<MaterializedCompiledArtifact>> lazy)
    {
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
        {
            _entries.TryRemove(key, out _);
        }
    }

    private static void DisposeWhenMaterialized(Lazy<Task<MaterializedCompiledArtifact>> lazy)
    {
        if (!lazy.IsValueCreated)
        {
            return;
        }

        var task = lazy.Value;
        if (task.IsCompletedSuccessfully)
        {
            task.Result.Dispose();
            return;
        }

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Status == TaskStatus.RanToCompletion)
                {
                    completed.Result.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
