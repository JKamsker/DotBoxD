using System.Text.Json.Serialization;

namespace DotBoxD.Queryable.Ast;

/// <summary>
/// The node kind of a portable <see cref="QueryFilter"/>. The set is intentionally small and closed so a
/// host can interpret, index, and validate a captured filter without arbitrary expression support.
/// </summary>
public enum QueryFilterKind
{
    /// <summary>An always-true predicate (no constraint).</summary>
    [JsonStringEnumMemberName("all")]
    MatchAll = 0,

    /// <summary>Conjunction of <see cref="QueryFilter.Children"/>.</summary>
    [JsonStringEnumMemberName("and")]
    And = 1,

    /// <summary>Disjunction of <see cref="QueryFilter.Children"/>.</summary>
    [JsonStringEnumMemberName("or")]
    Or = 2,

    /// <summary>Negation of the single child in <see cref="QueryFilter.Children"/>.</summary>
    [JsonStringEnumMemberName("not")]
    Not = 3,

    /// <summary>A field/operator/value comparison.</summary>
    [JsonStringEnumMemberName("compare")]
    Compare = 4,

    /// <summary>Membership of a field in the constant set <see cref="QueryFilter.Values"/>.</summary>
    [JsonStringEnumMemberName("in")]
    In = 5,
}
