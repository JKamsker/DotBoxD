using System.Globalization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

internal sealed class QueryMembershipLookup
{
    // The probe covers 1/4/8/32/64/256 candidates. Below eight, keep the linear control.
    private const int MinimumCount = 8;
    private readonly IReadOnlyList<QueryValue> _values;
    private readonly bool _ignoreCase;
    private readonly HashSet<string>? _strings;
    private readonly HashSet<Guid>? _guids;
    private readonly HashSet<decimal>? _exact;
    private readonly HashSet<double>? _floating;

    private QueryMembershipLookup(QueryFilter filter)
    {
        _values = filter.Values;
        _ignoreCase = filter.IgnoreCase;
        if (_values.All(value => value.Kind == QueryValueKind.String))
        {
            _strings = new HashSet<string>(_values.Select(value => value.String!),
                _ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        else if (_values.All(value => value.Kind == QueryValueKind.Guid))
        {
            _guids = new HashSet<Guid>(_values.Select(value => value.Guid));
        }
        else if (_values.All(value => value.Kind is QueryValueKind.Integer or QueryValueKind.UnsignedInteger or QueryValueKind.Decimal))
        {
            _exact = new HashSet<decimal>(_values.Select(value => value.Kind switch
            {
                QueryValueKind.Integer => (decimal)value.Integer,
                QueryValueKind.UnsignedInteger => value.UnsignedInteger,
                _ => value.Decimal,
            }));
            _floating = new HashSet<double>(_exact.Select(value => (double)value));
        }
    }

    public static QueryMembershipLookup? TryCreate(QueryFilter filter)
    {
        // Raw initializers and replaced Values can remain mutable even after Compile. Only the
        // factory's privately owned snapshot can safely be indexed without changing that contract.
        if (filter.Values is not QueryValueSnapshot || filter.Values.Count < MinimumCount)
        {
            return null;
        }

        var lookup = new QueryMembershipLookup(filter);
        return lookup._strings is not null || lookup._guids is not null || lookup._exact is not null ? lookup : null;
    }

    public bool Contains(object? actual)
    {
        if (_strings is not null)
        {
            return actual is string text && (string.Equals(text, _values[0].String,
                _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || _strings.Contains(text));
        }

        if (_guids is not null)
        {
            return actual is Guid guid && (guid == _values[0].Guid || _guids.Contains(guid));
        }

        if (_exact is not null)
        {
            return ContainsNumeric(actual);
        }

        // Retain per-candidate conversion and its observable behavior for custom IConvertible,
        // and the original handling of null/incomparable values.
        return QueryValueComparer.IsAnyEqual(actual, _values, _ignoreCase);
    }

    private bool ContainsNumeric(object? actual)
    {
        if (actual is not null && QueryScalarCompiler.IsExactNumeric(actual.GetType()))
        {
            return _exact!.Contains(Convert.ToDecimal(actual, CultureInfo.InvariantCulture));
        }

        if (actual is float or double)
        {
            return _floating!.Contains(Convert.ToDouble(actual, CultureInfo.InvariantCulture));
        }

        return QueryValueComparer.IsAnyEqual(actual, _values, _ignoreCase);
    }
}
