namespace DotBoxD.Hosting.Execution.Compiled;

internal sealed class CompiledExecutableHotEntry : IDisposable
{
    private readonly object _gate = new();
    private CompiledExecutablePublication? _primary;
    private CompiledExecutablePublication? _secondary;
    private bool _disposed;

    public bool HasCapacity
        => Volatile.Read(ref _primary) is null || Volatile.Read(ref _secondary) is null;

    public bool TryGet(
        ExecutionPlan plan,
        string entrypoint,
        out CompiledExecutable executable)
    {
        var primary = Volatile.Read(ref _primary);
        if (primary is not null && primary.SourceEntry.Matches(plan, entrypoint))
        {
            executable = primary.Value with { MaterializationStatus = "Hit" };
            return true;
        }

        var secondary = Volatile.Read(ref _secondary);
        if (secondary is not null && secondary.SourceEntry.Matches(plan, entrypoint))
        {
            executable = secondary.Value with { MaterializationStatus = "Hit" };
            return true;
        }

        executable = default;
        return false;
    }

    public bool Matches(ExecutionPlan plan, string entrypoint)
    {
        var primary = Volatile.Read(ref _primary);
        if (primary is not null && primary.SourceEntry.Matches(plan, entrypoint))
        {
            return true;
        }

        var secondary = Volatile.Read(ref _secondary);
        return secondary is not null && secondary.SourceEntry.Matches(plan, entrypoint);
    }

    public bool TryPublish(CompiledExecutablePublication publication)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var primary = _primary;
            if (ReferenceEquals(primary?.SourceEntry, publication.SourceEntry))
            {
                return true;
            }

            var secondary = _secondary;
            if (ReferenceEquals(secondary?.SourceEntry, publication.SourceEntry))
            {
                return true;
            }

            if (secondary is not null)
            {
                return false;
            }

            Volatile.Write(ref _secondary, primary);
            Volatile.Write(ref _primary, publication);
            return true;
        }
    }

    public void Invalidate(CompiledExecutableExecutionEntry entry)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_primary?.SourceEntry, entry))
            {
                Volatile.Write(ref _primary, null);
                var secondary = _secondary;
                Volatile.Write(ref _secondary, null);
                Volatile.Write(ref _primary, secondary);
            }
            else if (ReferenceEquals(_secondary?.SourceEntry, entry))
            {
                Volatile.Write(ref _secondary, null);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Volatile.Write(ref _primary, null);
            Volatile.Write(ref _secondary, null);
        }
    }

}

internal sealed record CompiledExecutablePublication(
    CompiledExecutableExecutionEntry SourceEntry,
    CompiledExecutable Value);
