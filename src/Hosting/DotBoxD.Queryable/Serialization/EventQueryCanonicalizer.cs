using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Produces a canonical form of a query so structurally-equivalent queries fingerprint identically.
/// Canonicalization is order-independent for commutative nodes: <see cref="QueryFilterKind.And"/> and
/// <see cref="QueryFilterKind.Or"/> children and <see cref="QueryFilterKind.In"/> values are sorted by
/// their canonical text. It does not reorder operands of non-commutative shapes or attempt logical
/// simplification (left to a later satisfiability pass).
/// </summary>
public static class EventQueryCanonicalizer
{
    /// <summary>Returns a canonicalized copy of <paramref name="document"/>.</summary>
    public static EventQueryDocument Canonicalize(EventQueryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EventQueryDocumentInvariants.RequireValidShape(document);
        return document with { Filter = Canonicalize(document.Filter) };
    }

    /// <summary>Returns a canonicalized copy of <paramref name="filter"/>.</summary>
    public static QueryFilter Canonicalize(QueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        switch (filter.Kind)
        {
            case QueryFilterKind.And:
            case QueryFilterKind.Or:
                var children = filter.Children
                    .Select(Canonicalize)
                    .OrderBy(SortKey, StringComparer.Ordinal)
                    .ToArray();
                return filter with { Children = children };
            case QueryFilterKind.Not:
                return filter with { Children = [Canonicalize(filter.Children[0])] };
            case QueryFilterKind.In:
                var values = filter.Values
                    .OrderBy(v => v.Kind)
                    .ThenBy(v => v.ToCanonicalText(), StringComparer.Ordinal)
                    .ToArray();
                return filter with { Values = values };
            default:
                return filter;
        }
    }

    private static string SortKey(QueryFilter filter) => QueryFingerprint.CanonicalText(filter);
}
