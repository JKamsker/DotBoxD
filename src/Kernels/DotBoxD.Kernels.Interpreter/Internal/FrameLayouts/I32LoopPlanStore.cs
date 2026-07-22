using System.Collections.Concurrent;
using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// Holds a frame layout's lazily initialized i32 loop-plan admission and cache state.
/// As an inline mutable value, the first observed statement needs no extra owner allocation.
/// </summary>
internal struct I32LoopPlanStore
{
    private ForRangeStatement? _candidate;
    private ConcurrentDictionary<ForRangeStatement, byte>? _candidateIndex;
    private I32ForLoopPlanCache? _plans;

    public bool TryGet(
        ForRangeStatement statement,
        InterpreterFrame frame,
        int loopSlot,
        out I32ForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            plan = null!;
            return false;
        }

        return cache.TryGet(statement, frame, loopSlot, out plan);
    }

    public bool ShouldCache(ForRangeStatement statement)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache?.Contains(statement) == true)
        {
            return false;
        }

        var candidates = Volatile.Read(ref _candidateIndex);
        if (candidates is not null)
        {
            return !candidates.TryAdd(statement, 0);
        }

        return ShouldCacheBeforeCandidateIndex(statement);
    }

    private bool ShouldCacheBeforeCandidateIndex(ForRangeStatement statement)
    {
        var candidate = Volatile.Read(ref _candidate);
        if (ReferenceEquals(candidate, statement))
        {
            return true;
        }

        if (candidate is null)
        {
            var existingCandidate = Interlocked.CompareExchange(ref _candidate, statement, null);
            if (existingCandidate is null)
            {
                return false;
            }

            candidate = existingCandidate;
        }

        if (ReferenceEquals(candidate, statement))
        {
            return true;
        }

        return PromoteCandidates(candidate, statement);
    }

    private bool PromoteCandidates(ForRangeStatement firstCandidate, ForRangeStatement statement)
    {
        var created = new ConcurrentDictionary<ForRangeStatement, byte>(
            ReferenceEqualityComparer.Instance);
        created.TryAdd(firstCandidate, 0);
        created.TryAdd(statement, 0);
        var published = Interlocked.CompareExchange(ref _candidateIndex, created, null);
        if (published is null)
        {
            Interlocked.CompareExchange(ref _candidate, null, firstCandidate);
            return false;
        }

        return !published.TryAdd(statement, 0);
    }

    public void Cache(I32ForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _plans);
        if (cache is null)
        {
            var created = new I32ForLoopPlanCache();
            cache = Interlocked.CompareExchange(ref _plans, created, null) ?? created;
        }

        cache.Store(plan);
        Interlocked.CompareExchange(ref _candidate, null, plan.Statement);
    }
}
