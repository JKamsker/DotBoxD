using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Planning;

/// <summary>
/// A single index-covered constraint extracted from a query filter: a scalar member path, a comparison
/// operator, and the constant bound. Equality predicates are also routing keys (see
/// <see cref="EventQueryPlan.RoutingKeys"/>); range predicates describe an indexable bound.
/// </summary>
public sealed record IndexedPredicate
{
    /// <summary>The dotted member path the constraint applies to.</summary>
    public required string Path { get; init; }

    /// <summary>The comparison operator (equality or a range operator).</summary>
    public required QueryComparisonOperator Operator { get; init; }

    /// <summary>The constant bound.</summary>
    public required QueryValue Value { get; init; }

    /// <summary>Whether this predicate is an exact-equality routing key.</summary>
    public bool IsEquality => Operator == QueryComparisonOperator.Equal;
}
