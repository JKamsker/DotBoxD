using System.Runtime.CompilerServices;

namespace DotBoxD.Hosting.Execution.Prepared;

/// <summary>
/// Retains one reusable no-audit run state per prepared-plan identity and lazily admits one more
/// when executions overlap. A third overlapping run keeps using the fresh-state path, while idle
/// plan slots are evicted in admission order.
/// </summary>
internal sealed class CompiledNoAuditRunStatePool : IDisposable
{
    // Match the compiled executable cache's lifetime bound so plan churn cannot grow host retention.
    // With the lazily admitted second lane, the absolute retained-state bound is 128.
    internal const int Capacity = 64;

    private readonly ConditionalWeakTable<ExecutionPlan, CompiledNoAuditRunStateSlot> _slots = new();
    private readonly Queue<CompiledNoAuditRunStateSlot> _admissionOrder = new();
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
        if (slot is null)
        {
            return default;
        }

        CompiledNoAuditRunStateSlot lane;
        long generation;
        if (slot.TryTake(out generation))
        {
            lane = slot;
        }
        else if (slot.Secondary is { IsInUse: true } ||
                 !TryTakeSecondarySlow(plan, slot, out lane, out generation))
        {
            return default;
        }

        if (Volatile.Read(ref _disposed) == 0)
        {
            return new Lease(lane, generation);
        }

        lane.Release(generation);
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
                slot.Secondary?.Retire();
            }

            _slots.Clear();
            _admissionOrder.Clear();
        }
    }

    internal bool HasStateFor(ExecutionPlan plan)
        => _slots.TryGetValue(plan, out _);

    internal bool HasSecondaryStateFor(ExecutionPlan plan)
    {
        lock (_admissionGate)
        {
            return _slots.TryGetValue(plan, out var primary) && primary.Secondary is not null;
        }
    }

    private CompiledNoAuditRunStateSlot? GetOrCreateSlot(ExecutionPlan plan)
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

            var created = new CompiledNoAuditRunStateSlot(plan);
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
            var secondary = candidate.Secondary;
            if (secondary is { IsInUse: true })
            {
                _admissionOrder.Enqueue(candidate);
                continue;
            }

            if (!candidate.TryMarkEvicted())
            {
                _admissionOrder.Enqueue(candidate);
                continue;
            }

            // Secondary admission is serialized by this gate, and the active check above is
            // therefore stable. Retire remains fail-safe if that invariant changes later.
            secondary?.Retire();

            _slots.Remove(candidate.Plan);
            return true;
        }

        return false;
    }

    private bool TryTakeSecondarySlow(
        ExecutionPlan plan,
        CompiledNoAuditRunStateSlot primary,
        out CompiledNoAuditRunStateSlot lane,
        out long generation)
    {
        // Secondary admission and acquisition share the eviction gate. Primary acquisition stays
        // lock-free; retrying it under the gate avoids retaining a second state for a transient race.
        lock (_admissionGate)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                !_slots.TryGetValue(plan, out var current) ||
                !ReferenceEquals(current, primary) ||
                primary.IsRetired)
            {
                lane = null!;
                generation = 0;
                return false;
            }

            if (primary.TryTake(out generation))
            {
                lane = primary;
                return true;
            }

            var secondary = primary.GetOrCreateSecondary();
            if (secondary.TryTake(out generation))
            {
                lane = secondary;
                return true;
            }
        }

        lane = null!;
        generation = 0;
        return false;
    }

    internal readonly struct Lease : IDisposable
    {
        private readonly CompiledNoAuditRunStateSlot? _lane;
        private readonly long _generation;

        internal Lease(CompiledNoAuditRunStateSlot lane, long generation)
        {
            _lane = lane;
            _generation = generation;
        }

        // The host keeps this single-owner handle in a using-local. Generation checks make stale
        // copies harmless without adding a per-execution allocation.
        public CompiledNoAuditRunState? State
            => _lane is not null && _lane.IsHeldBy(_generation) ? _lane.State : null;

        public bool IsAcquired => _lane is not null && _lane.IsHeldBy(_generation);

        public void Dispose() => _lane?.Release(_generation);
    }
}
