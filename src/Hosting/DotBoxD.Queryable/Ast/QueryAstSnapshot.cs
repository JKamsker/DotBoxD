namespace DotBoxD.Queryable.Ast;

internal static class QueryAstSnapshot
{
    public static QueryFilter Filter(QueryFilter filter)
    {
        var snapshot = new QueryFilter
        {
            Kind = filter.Kind,
            Field = filter.Field,
            Value = filter.Value,
            Values = [.. filter.Values],
            Children = SnapshotChildren(filter.Children),
            IgnoreCase = filter.IgnoreCase,
        };

        return filter.HasOperator ? snapshot with { Operator = filter.Operator } : snapshot;
    }

    public static QueryProjection Projection(QueryProjection projection) => new()
    {
        Kind = projection.Kind,
        Path = projection.Path,
        TypeName = projection.TypeName,
        Fields = [.. projection.Fields],
    };

    private static IReadOnlyList<QueryFilter> SnapshotChildren(IReadOnlyList<QueryFilter> children)
    {
        var snapshot = new QueryFilter[children.Count];
        for (var i = 0; i < children.Count; i++)
        {
            snapshot[i] = Filter(children[i]);
        }

        return snapshot;
    }
}
