using System.Runtime.CompilerServices;

namespace DotBoxD.Hosting.Execution.Prepared;

/// <summary>
/// Retains at most one reusable no-audit run state per prepared-plan identity. A busy plan keeps
/// running through the fresh-state path, while idle slots are evicted in admission order.
/// </summary>
internal sealed class CompiledNoAuditRunStatePool : IDisposable
{
    // Match the compiled executable cache's lifetime bound so plan churn cannot grow host retention.
    internal const int Capacity = 64;

    private readonly ConditionalWeakTable<ExecutionPlan, Slot> _slots = new();
    private readonly Queue<Slot> _admissionOrder = new();
    private readonly object _admissionGate = new();
    private int _disposed;

    public Lease TryAcquire(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return default;
        }

        var slot = GetOrCreateSlot(plan);
        if (slot is null || !slot.TryTake(out var generation))
        {
            return default;
        }

        if (Volatile.Read(ref _disposed) == 0)
        {
            return new Lease(slot, generation);
        }

        slot.Release(generation);
        return default;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_admissionGate)
        {
            foreach (var slot in _admissionOrder)
            {
                slot.Retire();
            }

            _slots.Clear();
            _admissionOrder.Clear();
        }
    }

    internal bool HasStateFor(ExecutionPlan plan)
        => _slots.TryGetValue(plan, out _);

    private Slot? GetOrCreateSlot(ExecutionPlan plan)
    {
        if (_slots.TryGetValue(plan, out var existing))
        {
            return existing;
        }

        lock (_admissionGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return null;
            }

            if (_slots.TryGetValue(plan, out existing))
            {
                return existing;
            }

            if (_admissionOrder.Count >= Capacity && !TryEvictOldestIdleSlot())
            {
                return null;
            }

            var created = new Slot(plan);
            _slots.Add(plan, created);
            _admissionOrder.Enqueue(created);
            return created;
        }
    }

    private bool TryEvictOldestIdleSlot()
    {
        var candidateCount = _admissionOrder.Count;
        for (var i = 0; i < candidateCount; i++)
        {
            var candidate = _admissionOrder.Dequeue();
            if (!candidate.TryMarkEvicted())
            {
                _admissionOrder.Enqueue(candidate);
                continue;
            }

            _slots.Remove(candidate.Plan);
            return true;
        }

        return false;
    }

    internal readonly struct Lease : IDisposable
    {
        private readonly Slot? _slot;
        private readonly long _generation;

        internal Lease(Slot slot, long generation)
        {
            _slot = slot;
            _generation = generation;
        }

        // The host keeps this single-owner handle in a using-local. Generation checks make stale
        // copies harmless without adding a per-execution allocation.
        public CompiledNoAuditRunState? State
            => _slot is not null && _slot.IsHeldBy(_generation) ? _slot.State : null;

        public bool IsAcquired => _slot is not null && _slot.IsHeldBy(_generation);

        public void Dispose() => _slot?.Release(_generation);
    }

    internal sealed class Slot(ExecutionPlan plan)
    {
        private const long Retired = long.MinValue;
        private long _leaseState;
        private long _nextGeneration;

        public ExecutionPlan Plan { get; } = plan;

        public CompiledNoAuditRunState State { get; } = new(plan);

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
}
