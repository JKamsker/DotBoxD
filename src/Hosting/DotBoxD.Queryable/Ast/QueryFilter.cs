namespace DotBoxD.Queryable.Ast;

/// <summary>
/// A node in the portable filter AST. A single record type carries every <see cref="QueryFilterKind"/>
/// (a tagged union): leaf comparisons use <see cref="Field"/>/<see cref="Operator"/>/<see cref="Value"/>,
/// <see cref="QueryFilterKind.In"/> uses <see cref="Field"/>/<see cref="Values"/>, and the boolean
/// connectives use <see cref="Children"/>. Construct nodes through the static factories so invariants
/// (child counts, required fields) stay consistent.
/// </summary>
public sealed record QueryFilter
{
    private static readonly IReadOnlyList<QueryFilter> NoChildren = [];
    private static readonly IReadOnlyList<QueryValue> NoValues = [];

    /// <summary>The node kind.</summary>
    public required QueryFilterKind Kind { get; init; }

    /// <summary>The dotted member path for <see cref="QueryFilterKind.Compare"/>/<see cref="QueryFilterKind.In"/> nodes.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>The operator for a <see cref="QueryFilterKind.Compare"/> node.</summary>
    public QueryComparisonOperator Operator { get; init; }

    /// <summary>The right-hand literal for a <see cref="QueryFilterKind.Compare"/> node.</summary>
    public QueryValue? Value { get; init; }

    /// <summary>The candidate set for a <see cref="QueryFilterKind.In"/> node.</summary>
    public IReadOnlyList<QueryValue> Values { get; init; } = NoValues;

    /// <summary>The operands for <see cref="QueryFilterKind.And"/>/<see cref="QueryFilterKind.Or"/>/<see cref="QueryFilterKind.Not"/> nodes.</summary>
    public IReadOnlyList<QueryFilter> Children { get; init; } = NoChildren;

    /// <summary>Whether a string comparison should ignore case (ordinal-ignore-case).</summary>
    public bool IgnoreCase { get; init; }

    /// <summary>The always-true predicate.</summary>
    public static QueryFilter MatchAll { get; } = new() { Kind = QueryFilterKind.MatchAll };

    /// <summary>Builds a comparison node.</summary>
    public static QueryFilter Compare(
        string field,
        QueryComparisonOperator op,
        QueryValue value,
        bool ignoreCase = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentNullException.ThrowIfNull(value);
        return new QueryFilter
        {
            Kind = QueryFilterKind.Compare,
            Field = field,
            Operator = op,
            Value = value,
            IgnoreCase = ignoreCase,
        };
    }

    /// <summary>Builds a set-membership node.</summary>
    public static QueryFilter In(string field, IReadOnlyList<QueryValue> values, bool ignoreCase = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentNullException.ThrowIfNull(values);
        return new QueryFilter
        {
            Kind = QueryFilterKind.In,
            Field = field,
            Values = values,
            IgnoreCase = ignoreCase,
        };
    }

    /// <summary>Builds a conjunction; an empty operand list collapses to <see cref="MatchAll"/>.</summary>
    public static QueryFilter And(IReadOnlyList<QueryFilter> children) => Connective(QueryFilterKind.And, children);

    /// <summary>Builds a disjunction.</summary>
    public static QueryFilter Or(IReadOnlyList<QueryFilter> children) => Connective(QueryFilterKind.Or, children);

    /// <summary>Builds a negation of <paramref name="child"/>.</summary>
    public static QueryFilter Not(QueryFilter child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return new QueryFilter { Kind = QueryFilterKind.Not, Children = [child] };
    }

    private static QueryFilter Connective(QueryFilterKind kind, IReadOnlyList<QueryFilter> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        // Flatten nested same-kind nodes so a left-associative chain (a && b && c, parsed as (a && b) && c)
        // becomes a single flat And/Or. This keeps the AST canonical (one-level planning sees every leaf)
        // and makes fingerprints independent of how the expression was parenthesized.
        var flattened = new List<QueryFilter>(children.Count);
        foreach (var child in children)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (child.Kind == kind)
            {
                flattened.AddRange(child.Children);
            }
            else
            {
                flattened.Add(child);
            }
        }

        if (flattened.Count == 0)
        {
            return MatchAll;
        }

        return flattened.Count == 1
            ? flattened[0]
            : new QueryFilter { Kind = kind, Children = flattened };
    }
}
