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
        => IsEqualityOrOrderingOperator(op) || IsStringOperator(op)
            ? op
            : throw UnknownCompareOperator(op);

    private static bool IsEqualityOrOrderingOperator(QueryComparisonOperator op)
        => op is QueryComparisonOperator.Equal or
            QueryComparisonOperator.NotEqual or
            QueryComparisonOperator.GreaterThan or
            QueryComparisonOperator.GreaterThanOrEqual or
            QueryComparisonOperator.LessThan or
            QueryComparisonOperator.LessThanOrEqual;

    private static bool IsStringOperator(QueryComparisonOperator op)
        => op is QueryComparisonOperator.StringContains or
            QueryComparisonOperator.StringStartsWith or
            QueryComparisonOperator.StringEndsWith;

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
        foreach (var property in InactiveProperties(
            filter,
            hasField,
            hasOperator,
            hasValue,
            hasValues,
            hasChildren,
            hasIgnoreCase))
        {
            if (property.IsInactive)
            {
                inactive = AddInactive(inactive, property.Name);
            }
        }

        if (inactive is not null)
        {
            throw new InvalidOperationException(
                $"QueryFilter {kind} nodes cannot carry inactive union-arm properties: {inactive}.");
        }
    }

    private static InactiveProperty[] InactiveProperties(
        QueryFilter filter,
        bool hasField,
        bool hasOperator,
        bool hasValue,
        bool hasValues,
        bool hasChildren,
        bool hasIgnoreCase) =>
        [
            new(nameof(QueryFilter.Field), hasField && !string.IsNullOrEmpty(filter.Field)),
            new(nameof(QueryFilter.Operator), hasOperator && filter.HasOperator),
            new(nameof(QueryFilter.Value), hasValue && filter.Value is not null),
            new(nameof(QueryFilter.Values), hasValues && filter.Values.Count > 0),
            new(nameof(QueryFilter.Children), hasChildren && filter.Children.Count > 0),
            new(nameof(QueryFilter.IgnoreCase), hasIgnoreCase && filter.IgnoreCase)
        ];

    private static string AddInactive(string? inactive, string property)
        => inactive is null ? property : $"{inactive}, {property}";

    private readonly record struct InactiveProperty(string Name, bool IsInactive);

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

    internal static bool IsValidFieldPath(string field)
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
