namespace DotBoxD.Queryable.Ast;

internal static class QueryFilterInvariants
{
    public static QueryFilterKind RequireKnownKind(QueryFilter filter)
        => filter.Kind switch
        {
            QueryFilterKind.MatchAll or
            QueryFilterKind.And or
            QueryFilterKind.Or or
            QueryFilterKind.Not or
            QueryFilterKind.Compare or
            QueryFilterKind.In => filter.Kind,
            _ => throw UnknownKind(filter.Kind),
        };

    public static QueryValue CompareValue(QueryFilter filter)
    {
        if (!filter.HasOperator)
        {
            throw MissingCompareOperator();
        }

        _ = RequireKnownCompareOperator(filter.Operator);
        return filter.Value ?? throw MissingCompareValue();
    }

    public static QueryComparisonOperator RequireKnownCompareOperator(QueryComparisonOperator op)
        => op switch
        {
            QueryComparisonOperator.Equal or
            QueryComparisonOperator.NotEqual or
            QueryComparisonOperator.GreaterThan or
            QueryComparisonOperator.GreaterThanOrEqual or
            QueryComparisonOperator.LessThan or
            QueryComparisonOperator.LessThanOrEqual or
            QueryComparisonOperator.StringContains or
            QueryComparisonOperator.StringStartsWith or
            QueryComparisonOperator.StringEndsWith => op,
            _ => throw UnknownCompareOperator(op),
        };

    public static void RequireCompareValues(QueryFilter filter)
    {
        if (filter.Kind == QueryFilterKind.Compare)
        {
            _ = CompareValue(filter);
            return;
        }

        foreach (var child in filter.Children)
        {
            RequireCompareValues(child);
        }
    }

    public static void RequireValidShape(QueryFilter filter)
    {
        var kind = RequireKnownKind(filter);
        switch (kind)
        {
            case QueryFilterKind.MatchAll:
                RejectInactiveArmProperties(
                    filter,
                    kind,
                    hasField: true,
                    hasOperator: true,
                    hasValue: true,
                    hasValues: true,
                    hasChildren: true,
                    hasIgnoreCase: true);
                break;
            case QueryFilterKind.And:
            case QueryFilterKind.Or:
                RequireBooleanChildren(filter, kind);
                RejectInactiveArmProperties(
                    filter,
                    kind,
                    hasField: true,
                    hasOperator: true,
                    hasValue: true,
                    hasValues: true,
                    hasChildren: false,
                    hasIgnoreCase: true);
                break;
            case QueryFilterKind.Compare:
                RequireFieldPath(filter, "Compare");
                _ = CompareValue(filter);
                RejectInactiveArmProperties(
                    filter,
                    kind,
                    hasField: false,
                    hasOperator: false,
                    hasValue: false,
                    hasValues: true,
                    hasChildren: true,
                    hasIgnoreCase: false);
                break;
            case QueryFilterKind.In:
                RequireFieldPath(filter, "In");
                RequireInValues(filter);
                RejectInactiveArmProperties(
                    filter,
                    kind,
                    hasField: false,
                    hasOperator: true,
                    hasValue: true,
                    hasValues: false,
                    hasChildren: true,
                    hasIgnoreCase: false);
                break;
            case QueryFilterKind.Not:
                RequireNotChild(filter);
                RejectInactiveArmProperties(
                    filter,
                    kind,
                    hasField: true,
                    hasOperator: true,
                    hasValue: true,
                    hasValues: true,
                    hasChildren: false,
                    hasIgnoreCase: true);
                break;
        }

        for (var i = 0; i < filter.Children.Count; i++)
        {
            var child = filter.Children[i] ?? throw NullChild(kind, i);
            RequireValidShape(child);
        }
    }

    private static void RequireInValues(QueryFilter filter)
    {
        for (var i = 0; i < filter.Values.Count; i++)
        {
            if (filter.Values[i] is null)
            {
                throw new InvalidOperationException(
                    "QueryFilter In nodes require Values to contain only non-null QueryValue elements.");
            }
        }
    }

    private static void RejectInactiveArmProperties(
        QueryFilter filter,
        QueryFilterKind kind,
        bool hasField,
        bool hasOperator,
        bool hasValue,
        bool hasValues,
        bool hasChildren,
        bool hasIgnoreCase)
    {
        string? inactive = null;
        if (hasField && !string.IsNullOrEmpty(filter.Field))
        {
            inactive = AddInactive(inactive, nameof(QueryFilter.Field));
        }

        if (hasOperator && filter.HasOperator)
        {
            inactive = AddInactive(inactive, nameof(QueryFilter.Operator));
        }

        if (hasValue && filter.Value is not null)
        {
            inactive = AddInactive(inactive, nameof(QueryFilter.Value));
        }

        if (hasValues && filter.Values.Count > 0)
        {
            inactive = AddInactive(inactive, nameof(QueryFilter.Values));
        }

        if (hasChildren && filter.Children.Count > 0)
        {
            inactive = AddInactive(inactive, nameof(QueryFilter.Children));
        }

        if (hasIgnoreCase && filter.IgnoreCase)
        {
            inactive = AddInactive(inactive, nameof(QueryFilter.IgnoreCase));
        }

        if (inactive is not null)
        {
            throw new InvalidOperationException(
                $"QueryFilter {kind} nodes cannot carry inactive union-arm properties: {inactive}.");
        }
    }

    private static string AddInactive(string? inactive, string property)
        => inactive is null ? property : $"{inactive}, {property}";

    private static void RequireNotChild(QueryFilter filter)
    {
        if (filter.Children.Count != 1)
        {
            throw new InvalidOperationException("QueryFilter Not nodes require exactly one child.");
        }
    }

    private static void RequireBooleanChildren(QueryFilter filter, QueryFilterKind kind)
    {
        if (filter.Children.Count == 0)
        {
            throw new InvalidOperationException($"QueryFilter {kind} nodes require non-empty terms.");
        }
    }

    private static void RequireFieldPath(QueryFilter filter, string kind)
    {
        if (IsValidFieldPath(filter.Field))
        {
            return;
        }

        throw new InvalidOperationException(
            $"QueryFilter {kind} nodes require a non-empty Field path with identifier segments.");
    }

    private static bool IsValidFieldPath(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return false;
        }

        foreach (var segment in field.Split('.'))
        {
            if (!IsIdentifierSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierSegment(string segment)
    {
        if (segment.Length == 0 || !(char.IsLetter(segment[0]) || segment[0] == '_'))
        {
            return false;
        }

        for (var i = 1; i < segment.Length; i++)
        {
            if (!char.IsLetterOrDigit(segment[i]) && segment[i] != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidOperationException MissingCompareValue()
        => new("QueryFilter Compare nodes require Value.");

    private static InvalidOperationException MissingCompareOperator()
        => new("QueryFilter Compare nodes require Operator.");

    private static InvalidOperationException UnknownKind(QueryFilterKind kind)
        => new($"QueryFilter has unsupported Kind value '{(int)kind}'.");

    private static InvalidOperationException UnknownCompareOperator(QueryComparisonOperator op)
        => new($"QueryFilter Compare node has unsupported Operator value '{(int)op}'.");

    private static InvalidOperationException NullChild(QueryFilterKind kind, int index)
        => new($"QueryFilter {kind} nodes cannot contain a null child at index {index}.");
}
