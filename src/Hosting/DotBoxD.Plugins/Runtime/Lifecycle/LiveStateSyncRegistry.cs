namespace DotBoxD.Plugins.Runtime.Lifecycle;

internal sealed class LiveStateSyncRegistry(Func<Type, LiveUpdateMode> getUpdateMode)
{
    private readonly object _registrationGate = new();
    // Registration is rare. Readers treat every published array as immutable so hot syncs do not copy it.
    private LiveStateSynchronizer[] _synchronizers = [];

    public void Register(Type stateType, Action synchronize)
    {
        lock (_registrationGate)
        {
            var current = _synchronizers;
            var updated = new LiveStateSynchronizer[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[^1] = new LiveStateSynchronizer(stateType, synchronize);
            Volatile.Write(ref _synchronizers, updated);
        }
    }

    public IReadOnlyList<Action> SynchronizeForInput()
    {
        List<Action>? deferredUpdates = null;
        foreach (var synchronizer in Snapshot())
        {
            var mode = getUpdateMode(synchronizer.StateType);
            if ((mode & LiveUpdateMode.AsyncSet) == LiveUpdateMode.AsyncSet)
            {
                (deferredUpdates ??= []).Add(synchronizer.Synchronize);
                continue;
            }

            synchronizer.Synchronize();
        }

        return deferredUpdates is null ? Array.Empty<Action>() : deferredUpdates;
    }

    public void SynchronizeForFlush()
    {
        foreach (var synchronizer in Snapshot())
        {
            if ((getUpdateMode(synchronizer.StateType) & LiveUpdateMode.AsyncSet) == LiveUpdateMode.AsyncSet)
            {
                synchronizer.Synchronize();
            }
        }
    }

    private LiveStateSynchronizer[] Snapshot()
        => Volatile.Read(ref _synchronizers);

    private sealed record LiveStateSynchronizer(Type StateType, Action Synchronize);
}
