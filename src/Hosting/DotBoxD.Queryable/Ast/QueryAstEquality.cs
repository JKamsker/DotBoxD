namespace DotBoxD.Queryable.Ast;

internal static class QueryAstEquality
{
    public static bool FilterEquals(QueryFilter? left, QueryFilter? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && left.Kind == right.Kind
            && left.Field == right.Field
            && left.HasOperator == right.HasOperator
            && left.Operator == right.Operator
            && Equals(left.Value, right.Value)
            && left.IgnoreCase == right.IgnoreCase
            && ListsEqual(left.Values, right.Values)
            && FiltersEqual(left.Children, right.Children);
    }

    public static bool ProjectionEquals(QueryProjection? left, QueryProjection? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && left.Kind == right.Kind
            && left.Path == right.Path
            && left.TypeName == right.TypeName
            && ListsEqual(left.Fields, right.Fields);
    }

    public static int FilterHashCode(QueryFilter? filter)
    {
        if (filter is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(filter.Kind);
        hash.Add(filter.Field);
        hash.Add(filter.HasOperator);
        hash.Add(filter.Operator);
        hash.Add(filter.Value);
        hash.Add(filter.IgnoreCase);
        AddList(ref hash, filter.Values);
        AddFilters(ref hash, filter.Children);
        return hash.ToHashCode();
    }

    public static int ProjectionHashCode(QueryProjection? projection)
    {
        if (projection is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(projection.Kind);
        hash.Add(projection.Path);
        hash.Add(projection.TypeName);
        AddList(ref hash, projection.Fields);
        return hash.ToHashCode();
    }

    private static bool FiltersEqual(IReadOnlyList<QueryFilter> left, IReadOnlyList<QueryFilter> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!FilterEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ListsEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddFilters(ref HashCode hash, IReadOnlyList<QueryFilter> filters)
    {
        hash.Add(filters.Count);
        foreach (var filter in filters)
        {
            hash.Add(FilterHashCode(filter));
        }
    }

    private static void AddList<T>(ref HashCode hash, IReadOnlyList<T> values)
    {
        hash.Add(values.Count);
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}
