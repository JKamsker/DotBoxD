using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Planning;

/// <summary>
/// The host-side plan for a query: the index-covered predicates a host can push into an index, the
/// equality subset usable as dispatch routing keys, the residual filter to evaluate on candidates (when
/// the index does not fully cover the filter), and the overall <see cref="IndexCoverage"/>. The plan lets a
/// host prefilter events by indexable constraints instead of running every subscription against every
/// event.
/// </summary>
public sealed record EventQueryPlan
{
    /// <summary>All index-covered predicates (equality and range).</summary>
    public required IReadOnlyList<IndexedPredicate> IndexedPredicates { get; init; }

    /// <summary>The equality predicates usable as exact-match routing keys.</summary>
    public required IReadOnlyList<IndexedPredicate> RoutingKeys { get; init; }

    /// <summary>The residual filter to evaluate after index prefiltering, or <see langword="null"/> when fully covered.</summary>
    public QueryFilter? ResidualFilter { get; init; }

    /// <summary>How much of the filter the index covers.</summary>
    public required IndexCoverage Coverage { get; init; }

    /// <summary>Whether the plan exposes at least one exact-match routing key for indexed dispatch.</summary>
    public bool IsRoutable => RoutingKeys.Count > 0;
}
