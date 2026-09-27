using System.Runtime.CompilerServices;

namespace DotBoxD.Hosting.Execution;

/// <summary>
/// Remembers the plans a host prepared so repeated execution of an already-prepared plan can be
/// integrity-checked in O(1) against the trusted prepared identity instead of re-validating,
/// re-canonical-hashing, and re-sealing the whole module on every dispatch (ALG-0013).
/// </summary>
/// <remarks>
/// Trusted plans are weakly keyed by their seal objects. A live original or copy keeps its seal
/// and trusted identity available; unused seals and plans can be collected together. Only plans
/// this host produced via <c>PrepareAsync</c> are registered, and incoming fields are compared
/// against that trusted identity (see <see cref="ExecutionPlanGuard"/>). A missing entry falls
/// back to the full rebuild-and-compare path, preserving validation independently of cache lifetime.
/// </remarks>
internal sealed class PreparedPlanIntegrityCache
{
    private static readonly TrustedReferenceMarker Marker = new();

    private readonly ConditionalWeakTable<ExecutionPlan, TrustedReferenceMarker> _trustedReferences = new();
    private ConditionalWeakTable<ExecutionPlanSeal, ExecutionPlan>? _trusted;

    public void Register(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _trustedReferences.Remove(plan);
        _trustedReferences.Add(plan, Marker);
        var trusted = LazyInitializer.EnsureInitialized(ref _trusted, static () => new());
        trusted.AddOrUpdate(plan.PlanSeal, plan);
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
