using System.Text.Json.Serialization;

namespace DotBoxD.Queryable.Ast;

/// <summary>
/// The comparison and string-match operators supported by a <see cref="QueryFilterKind.Compare"/> node.
/// JSON member names use the short tokens a host sees on the wire (<c>eq</c>, <c>gte</c>, …).
/// </summary>
public enum QueryComparisonOperator
{
    /// <summary>Field equals value.</summary>
    [JsonStringEnumMemberName("eq")]
    Equal = 0,

    /// <summary>Field does not equal value.</summary>
    [JsonStringEnumMemberName("neq")]
    NotEqual = 1,

    /// <summary>Field is strictly greater than value.</summary>
    [JsonStringEnumMemberName("gt")]
    GreaterThan = 2,

    /// <summary>Field is greater than or equal to value.</summary>
    [JsonStringEnumMemberName("gte")]
    GreaterThanOrEqual = 3,

    /// <summary>Field is strictly less than value.</summary>
    [JsonStringEnumMemberName("lt")]
    LessThan = 4,

    /// <summary>Field is less than or equal to value.</summary>
    [JsonStringEnumMemberName("lte")]
    LessThanOrEqual = 5,

    /// <summary>String field contains the value substring (ordinal).</summary>
    [JsonStringEnumMemberName("contains")]
    StringContains = 6,

    /// <summary>String field starts with the value (ordinal).</summary>
    [JsonStringEnumMemberName("startsWith")]
    StringStartsWith = 7,

    /// <summary>String field ends with the value (ordinal).</summary>
    [JsonStringEnumMemberName("endsWith")]
    StringEndsWith = 8,
}
