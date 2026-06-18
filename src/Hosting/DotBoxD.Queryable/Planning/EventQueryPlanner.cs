using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Planning;

/// <summary>
/// Extracts an <see cref="EventQueryPlan"/> from a filter AST. The planner recognizes a conjunction (or a
/// single leaf) of scalar comparisons: equality and range operators become index-covered predicates
/// (equality also becomes a routing key), while negations, string matches, set membership, and any
/// disjunction/nesting fall to the residual filter. Filters that are not a conjunction of leaves are
/// treated as fully residual.
/// </summary>
public static class EventQueryPlanner
{
    /// <summary>Plans the filter of a document.</summary>
    public static EventQueryPlan Plan(EventQueryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Plan(document.Filter);
    }

    /// <summary>Plans a filter AST into index-covered predicates plus a residual.</summary>
    public static EventQueryPlan Plan(QueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Kind == QueryFilterKind.MatchAll)
        {
            return new EventQueryPlan
            {
                IndexedPredicates = [],
                RoutingKeys = [],
                ResidualFilter = null,
                Coverage = IndexCoverage.Full,
            };
        }

        if (filter.Kind is QueryFilterKind.Or or QueryFilterKind.Not)
        {
            return new EventQueryPlan
            {
                IndexedPredicates = [],
                RoutingKeys = [],
                ResidualFilter = filter,
                Coverage = IndexCoverage.None,
            };
        }

        var terms = filter.Kind == QueryFilterKind.And ? filter.Children : [filter];
        var indexed = new List<IndexedPredicate>();
        var routing = new List<IndexedPredicate>();
        var residual = new List<QueryFilter>();

        foreach (var term in terms)
        {
            if (TryIndex(term, out var predicate))
            {
                indexed.Add(predicate);
                if (predicate.IsEquality)
                {
                    routing.Add(predicate);
                }
            }
            else
            {
                residual.Add(term);
            }
        }

        var coverage = residual.Count == 0
            ? IndexCoverage.Full
            : indexed.Count == 0 ? IndexCoverage.None : IndexCoverage.Partial;

        return new EventQueryPlan
        {
            IndexedPredicates = indexed,
            RoutingKeys = routing,
            ResidualFilter = residual.Count == 0 ? null : QueryFilter.And(residual),
            Coverage = coverage,
        };
    }

    private static bool TryIndex(QueryFilter term, out IndexedPredicate predicate)
    {
        predicate = null!;
        if (term.Kind != QueryFilterKind.Compare || term.Value is null || term.IgnoreCase)
        {
            return false;
        }

        // A null bound is not indexable: a Null routing key cannot be produced from a runtime member read,
        // so such a predicate must fall to the residual/broad path to be evaluated against every event.
        if (term.Value.Kind == QueryValueKind.Null)
        {
            return false;
        }

        if (!IsIndexableOperator(term.Operator))
        {
            return false;
        }

        predicate = new IndexedPredicate
        {
            Path = term.Field,
            Operator = term.Operator,
            Value = term.Value,
        };
        return true;
    }

    private static bool IsIndexableOperator(QueryComparisonOperator op) => op switch
    {
        QueryComparisonOperator.Equal => true,
        QueryComparisonOperator.GreaterThan => true,
        QueryComparisonOperator.GreaterThanOrEqual => true,
        QueryComparisonOperator.LessThan => true,
        QueryComparisonOperator.LessThanOrEqual => true,
        _ => false,
    };
}
