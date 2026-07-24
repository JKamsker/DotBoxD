namespace DotBoxD.Hosting.Execution.Prepared;

/// <summary>
/// Owns one reusable compiled run state and protects it with generation-checked exclusive leases.
/// Retirement preserves an active lease until its exact generation releases.
/// </summary>
internal sealed class CompiledNoAuditRunStateSlot(ExecutionPlan plan)
{
    private const long Retired = long.MinValue;
    private long _leaseState;
    private long _nextGeneration;
    private CompiledNoAuditRunStateSlot? _secondary;

    public ExecutionPlan Plan { get; } = plan;

    public CompiledNoAuditRunState State { get; } = new(plan);

    public bool IsInUse => Volatile.Read(ref _leaseState) > 0;

    public bool IsRetired => Volatile.Read(ref _leaseState) < 0;

    public CompiledNoAuditRunStateSlot? Secondary => Volatile.Read(ref _secondary);

    // The pool serializes creation with its admission gate. Volatile publication lets the
    // saturated fast path observe the fully constructed lane without entering that gate.
    public CompiledNoAuditRunStateSlot GetOrCreateSecondary()
    {
        var secondary = Volatile.Read(ref _secondary);
        if (secondary is not null)
        {
            return secondary;
        }

        secondary = new CompiledNoAuditRunStateSlot(Plan);
        Volatile.Write(ref _secondary, secondary);
        return secondary;
    }

    public bool TryTake(out long generation)
    {
        if (Volatile.Read(ref _leaseState) != 0)
        {
            generation = 0;
            return false;
        }

        var candidate = NextGeneration();
        if (Interlocked.CompareExchange(ref _leaseState, candidate, 0) == 0)
        {
            generation = candidate;
            return true;
        }

        generation = 0;
        return false;
    }

    public void Release(long generation)
    {
        while (true)
        {
            var current = Volatile.Read(ref _leaseState);
            if (current == generation)
            {
                if (Interlocked.CompareExchange(ref _leaseState, 0, generation) == generation)
                {
                    return;
                }

                continue;
            }

            var retiring = generation | Retired;
            if (current != retiring)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _leaseState, Retired, retiring) == retiring)
            {
                State.Dispose();
                return;
            }
        }
    }

    public bool IsHeldBy(long generation)
    {
        var current = Volatile.Read(ref _leaseState);
        return current == generation || current == (generation | Retired);
    }

    public bool TryMarkEvicted()
    {
        if (Interlocked.CompareExchange(ref _leaseState, Retired, 0) != 0)
        {
            return false;
        }

        State.Dispose();
        return true;
    }

    public void Retire()
    {
        while (true)
        {
            var current = Volatile.Read(ref _leaseState);
            if (current < 0)
            {
                return;
            }

            var retiring = current == 0 ? Retired : current | Retired;
            if (Interlocked.CompareExchange(ref _leaseState, retiring, current) != current)
            {
                continue;
            }

            if (current == 0)
            {
                State.Dispose();
            }

            return;
        }
    }

    private long NextGeneration()
    {
        while (true)
        {
            var candidate = Interlocked.Increment(ref _nextGeneration) & long.MaxValue;
            if (candidate != 0)
            {
                return candidate;
            }
        }
    }
}
