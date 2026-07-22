using System.Collections.Concurrent;
using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed partial class FunctionFrameLayout
{
    // Keep one-shot layouts allocation-free. A second distinct statement promotes
    // admission tracking to an index; actual plans use the same scalar-first shape.
    private ForRangeStatement? _i32LoopPlanCandidate;
    private ConcurrentDictionary<ForRangeStatement, byte>? _i32LoopPlanCandidates;
    private I32ForLoopPlanCache? _i32LoopPlans;

    public bool TryGetI32LoopPlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        int loopSlot,
        out I32ForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _i32LoopPlans);
        if (cache is null)
        {
            plan = null!;
            return false;
        }

        return cache.TryGet(statement, frame, loopSlot, out plan);
    }

    public bool ShouldCacheI32LoopPlan(ForRangeStatement statement)
    {
        var cache = Volatile.Read(ref _i32LoopPlans);
        if (cache?.Contains(statement) == true)
        {
            return false;
        }

        var candidates = Volatile.Read(ref _i32LoopPlanCandidates);
        if (candidates is not null)
        {
            return !candidates.TryAdd(statement, 0);
        }

        return ShouldCacheBeforeCandidateIndex(statement);
    }

    private bool ShouldCacheBeforeCandidateIndex(ForRangeStatement statement)
    {
        var candidate = Volatile.Read(ref _i32LoopPlanCandidate);
        if (ReferenceEquals(candidate, statement))
        {
            return true;
        }

        if (candidate is null)
        {
            var existingCandidate = Interlocked.CompareExchange(
                ref _i32LoopPlanCandidate,
                statement,
                null);
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

        return PromoteI32LoopPlanCandidates(candidate, statement);
    }

    private bool PromoteI32LoopPlanCandidates(
        ForRangeStatement firstCandidate,
        ForRangeStatement statement)
    {
        var created = new ConcurrentDictionary<ForRangeStatement, byte>(
            ReferenceEqualityComparer.Instance);
        created.TryAdd(firstCandidate, 0);
        created.TryAdd(statement, 0);
        var published = Interlocked.CompareExchange(
            ref _i32LoopPlanCandidates,
            created,
            null);
        if (published is null)
        {
            Interlocked.CompareExchange(ref _i32LoopPlanCandidate, null, firstCandidate);
            return false;
        }

        return !published.TryAdd(statement, 0);
    }

    public void CacheI32LoopPlan(I32ForLoopPlan plan)
    {
        var cache = Volatile.Read(ref _i32LoopPlans);
        if (cache is null)
        {
            var created = new I32ForLoopPlanCache();
            cache = Interlocked.CompareExchange(ref _i32LoopPlans, created, null) ?? created;
        }

        cache.Store(plan);
        Interlocked.CompareExchange(ref _i32LoopPlanCandidate, null, plan.Statement);
    }
}
