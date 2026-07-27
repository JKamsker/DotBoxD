namespace DotBoxD.Hosting.Execution.Compiled;

// The state is a null/live-hot/disposed union on the dedicated cache monitor. An empty object and
// this one-reference class have the same measured size, preserving the original cold allocation.
internal sealed class CompiledExecutableCacheSynchronization
{
    private static readonly object DisposedSentinel = new();
    private object? _hotState;

    public CompiledExecutableHotEntry GetOrCreateHotEntry(object owner)
    {
        if (_hotState is CompiledExecutableHotEntry hotEntry)
        {
            return hotEntry;
        }

        ThrowIfDisposed(owner);
        hotEntry = new CompiledExecutableHotEntry();
        Volatile.Write(ref _hotState, hotEntry);
        return hotEntry;
    }

    public bool TryGetHot(
        ExecutionPlan plan,
        string entrypoint,
        out CompiledExecutable executable)
    {
        if (Volatile.Read(ref _hotState) is CompiledExecutableHotEntry hotEntry)
        {
            return hotEntry.TryGet(plan, entrypoint, out executable);
        }

        executable = default;
        return false;
    }

    public bool HasHot(ExecutionPlan plan, string entrypoint)
        => Volatile.Read(ref _hotState) is CompiledExecutableHotEntry hotEntry &&
           hotEntry.Matches(plan, entrypoint);

    public bool HasHotCapacity
    {
        get
        {
            var state = Volatile.Read(ref _hotState);
            return state is null || state is CompiledExecutableHotEntry { HasCapacity: true };
        }
    }

    public bool TryBeginDispose(out CompiledExecutableHotEntry? hotEntry)
    {
        if (ReferenceEquals(_hotState, DisposedSentinel))
        {
            hotEntry = null;
            return false;
        }

        hotEntry = _hotState as CompiledExecutableHotEntry;
        Volatile.Write(ref _hotState, DisposedSentinel);
        return true;
    }

    public void ThrowIfDisposed(object owner)
        => ObjectDisposedException.ThrowIf(ReferenceEquals(_hotState, DisposedSentinel), owner);

    public void Invalidate(CompiledExecutableExecutionEntry entry)
    {
        if (Volatile.Read(ref _hotState) is CompiledExecutableHotEntry hotEntry)
        {
            hotEntry.Invalidate(entry);
        }
    }
}
