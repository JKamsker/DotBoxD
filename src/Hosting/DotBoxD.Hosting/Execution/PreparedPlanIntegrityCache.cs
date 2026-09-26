using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace DotBoxD.Hosting.Execution;

/// <summary>
/// Remembers the plans a host prepared so repeated execution of an already-prepared plan can be
/// integrity-checked in O(1) against the trusted prepared identity instead of re-validating,
/// re-canonical-hashing, and re-sealing the whole module on every dispatch (ALG-0013).
/// </summary>
/// <remarks>
/// A plan is keyed by its seal, which is an HMAC over the validated module/policy/binding identity
/// using the host signing key. Only plans this host produced via <c>PrepareAsync</c> are registered,
/// so a cache hit proves the seal is authentic; the cached entry is then the trusted reference the
/// incoming plan's fields are compared against (see <see cref="ExecutionPlanGuard"/>). A miss
/// (tampered seal, or a plan from another host) falls back to the full rebuild-and-compare path,
/// preserving every existing rejection.
/// </remarks>
internal sealed class PreparedPlanIntegrityCache
{
    private static readonly TrustedReferenceMarker Marker = new();

    private readonly ConditionalWeakTable<ExecutionPlan, TrustedReferenceMarker> _trustedReferences = new();
    private ConcurrentDictionary<ExecutionPlanSeal, ExecutionPlan>? _trusted;

    public void Register(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _trustedReferences.Remove(plan);
        _trustedReferences.Add(plan, Marker);
        var trusted = LazyInitializer.EnsureInitialized(ref _trusted, static () => new());
        trusted[plan.PlanSeal] = plan;
    }

    public bool ContainsTrustedReference(ExecutionPlan plan)
        => _trustedReferences.TryGetValue(plan, out _);

    public bool TryGetTrusted(ExecutionPlanSeal seal, out ExecutionPlan trusted)
    {
        var trustedPlans = Volatile.Read(ref _trusted);
        if (trustedPlans is not null)
        {
            return trustedPlans.TryGetValue(seal, out trusted!);
        }

        trusted = null!;
        return false;
    }

    private sealed class TrustedReferenceMarker;
}
