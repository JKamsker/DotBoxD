using System.Collections.Concurrent;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// Tracks exact statement identities until each one has produced two complete,
/// reusable plans. Plan storage remains owned by the shape-specific caller.
/// </summary>
internal struct TwoHitPlanAdmission<TStatement>
    where TStatement : class
{
    private TStatement? _candidate;
    private ConcurrentDictionary<TStatement, byte>? _candidateIndex;

    public bool ShouldCache(TStatement statement)
    {
        var candidates = Volatile.Read(ref _candidateIndex);
        if (candidates is not null)
        {
            return !candidates.TryAdd(statement, 0);
        }

        return ShouldCacheBeforeCandidateIndex(statement);
    }

    public void Forget(TStatement statement)
        => Interlocked.CompareExchange(ref _candidate, null, statement);

    private bool ShouldCacheBeforeCandidateIndex(TStatement statement)
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

    private bool PromoteCandidates(TStatement firstCandidate, TStatement statement)
    {
        var created = new ConcurrentDictionary<TStatement, byte>(ReferenceEqualityComparer.Instance);
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
}
