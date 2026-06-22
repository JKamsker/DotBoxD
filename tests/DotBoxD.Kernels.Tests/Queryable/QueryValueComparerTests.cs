using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

/// <summary>
/// Runtime-evaluation coverage for <see cref="QueryValueComparer"/> (previously untested at runtime), pinning
/// the review-hardening fixes: exact integral equality, incomparable <c>!=</c>, ignore-case ordering, and
/// non-throwing ulong-backed enum comparison.
/// </summary>
public sealed class QueryValueComparerTests
{
    private enum UlongBackedEnum : ulong
    {
        Max = ulong.MaxValue,
    }

    [Fact]
    public void Integer_equality_is_exact_above_two_pow_fifty_three()
    {
        // 9007199254740993 and 9007199254740992 are distinct longs that collapse to the same double.
        Assert.False(QueryValueComparer.AreEqual(9007199254740993L, QueryValue.FromInteger(9007199254740992L), ignoreCase: false));
        Assert.True(QueryValueComparer.AreEqual(9007199254740993L, QueryValue.FromInteger(9007199254740993L), ignoreCase: false));
    }

    [Fact]
    public void Integer_literal_still_interoperates_with_a_floating_point_member()
    {
        Assert.True(QueryValueComparer.AreEqual(5.0d, QueryValue.FromInteger(5), ignoreCase: false));
        Assert.True(QueryValueComparer.Compare(5.5d, QueryComparisonOperator.GreaterThan, QueryValue.FromInteger(5), ignoreCase: false));
    }

    [Fact]
    public void NotEqual_is_false_for_incomparable_operands()
    {
        // A numeric literal against a string member is incomparable: != must not report a match.
        Assert.False(QueryValueComparer.Compare("hello", QueryComparisonOperator.NotEqual, QueryValue.FromInteger(5), ignoreCase: false));
        Assert.False(QueryValueComparer.Compare("hello", QueryComparisonOperator.Equal, QueryValue.FromInteger(5), ignoreCase: false));
    }

    [Fact]
    public void NotEqual_is_true_for_a_null_member_versus_a_value()
    {
        Assert.True(QueryValueComparer.Compare(null, QueryComparisonOperator.NotEqual, QueryValue.FromInteger(5), ignoreCase: false));
        Assert.False(QueryValueComparer.Compare(null, QueryComparisonOperator.Equal, QueryValue.FromInteger(5), ignoreCase: false));
    }

    [Fact]
    public void Ordered_string_comparison_honors_ignore_case()
    {
        // 'B' vs 'a': case-insensitively 'b' > 'a' so LessThan is false; ordinally 'B'(0x42) < 'a'(0x61) so LessThan is true.
        Assert.False(QueryValueComparer.Compare("B", QueryComparisonOperator.LessThan, QueryValue.FromString("a"), ignoreCase: true));
        Assert.True(QueryValueComparer.Compare("B", QueryComparisonOperator.LessThan, QueryValue.FromString("a"), ignoreCase: false));
    }

    [Fact]
    public void Ulong_backed_enum_member_compares_without_overflowing()
    {
        // The enum value exceeds long.MaxValue; comparison widens to double instead of throwing OverflowException.
        Assert.True(QueryValueComparer.AreEqual(UlongBackedEnum.Max, QueryValue.FromNumber((double)ulong.MaxValue), ignoreCase: false));
    }
}
