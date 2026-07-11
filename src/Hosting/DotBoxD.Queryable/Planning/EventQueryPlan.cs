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
    private IReadOnlyList<IndexedPredicate> _indexedPredicates = null!;
    private IReadOnlyList<IndexedPredicate> _routingKeys = null!;
    private QueryFilter? _residualFilter;
    private IndexCoverage _coverage;
    private bool _residualFilterAssigned;
    private bool _coverageAssigned;

    /// <summary>All index-covered predicates (equality and range).</summary>
    public required IReadOnlyList<IndexedPredicate> IndexedPredicates
    {
        get => _indexedPredicates;
        init => _indexedPredicates = RequirePredicates(value, nameof(IndexedPredicates));
    }

    /// <summary>The equality predicates usable as exact-match routing keys.</summary>
    public required IReadOnlyList<IndexedPredicate> RoutingKeys
    {
        get => _routingKeys;
        init => _routingKeys = RequirePredicates(value, nameof(RoutingKeys));
    }

    /// <summary>The residual filter to evaluate after index prefiltering, or <see langword="null"/> when fully covered.</summary>
    public required QueryFilter? ResidualFilter
    {
        get => _residualFilter;
        init
        {
            _residualFilter = value;
            _residualFilterAssigned = true;
            ValidateResidualCoverageWhenComplete();
        }
    }

    /// <summary>How much of the filter the index covers.</summary>
    public required IndexCoverage Coverage
    {
        get => _coverage;
        init
        {
            EnsureKnownCoverage(value);
            _coverage = value;
            _coverageAssigned = true;
            ValidateResidualCoverageWhenComplete();
        }
    }

    /// <summary>Whether the plan exposes at least one exact-match routing key for indexed dispatch.</summary>
    public bool IsRoutable => RoutingKeys.Count > 0;

    private static IReadOnlyList<IndexedPredicate> RequirePredicates(
        IReadOnlyList<IndexedPredicate>? predicates,
        string paramName)
    {
        ArgumentNullException.ThrowIfNull(predicates, paramName);
        for (var i = 0; i < predicates.Count; i++)
        {
            if (predicates[i] is null)
            {
                throw new ArgumentException("Event query plan predicate collections cannot contain null entries.", paramName);
            }
        }

        return predicates;
    }

    private static void EnsureKnownCoverage(IndexCoverage coverage)
    {
        if (coverage is not (IndexCoverage.None or IndexCoverage.Partial or IndexCoverage.Full))
        {
            throw new ArgumentOutOfRangeException(nameof(Coverage), coverage, "Event query plan coverage is not defined.");
        }
    }

    private void ValidateResidualCoverageWhenComplete()
    {
        if (!_coverageAssigned || !_residualFilterAssigned)
        {
            return;
        }

        if ((_coverage == IndexCoverage.Full) == (_residualFilter is not null))
        {
            throw ResidualCoverageMismatch(_coverage);
        }
    }

    private static ArgumentException ResidualCoverageMismatch(IndexCoverage coverage)
        => new(
            coverage == IndexCoverage.Full
                ? "Fully covered event query plans cannot carry a residual filter."
                : "Partially or uncovered event query plans require a residual filter.",
            nameof(ResidualFilter));
}
