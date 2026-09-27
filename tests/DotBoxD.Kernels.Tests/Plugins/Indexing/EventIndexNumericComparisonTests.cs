using DotBoxD.Plugins;
using DotBoxD.Plugins.Indexing;

namespace DotBoxD.Kernels.Tests.Plugins.Indexing;

public sealed class EventIndexNumericComparisonTests
{
    [Theory]
    [InlineData(5, 5.4, IndexPredicateOperator.LessThan, true)]
    [InlineData(6, 5.6, IndexPredicateOperator.GreaterThan, true)]
    [InlineData(5, 5.4, IndexPredicateOperator.NotEquals, true)]
    [InlineData(5, 5.4, IndexPredicateOperator.Equals, false)]
    [InlineData(4, 5.4, IndexPredicateOperator.GreaterThan, false)]
    [InlineData(7, 5.6, IndexPredicateOperator.LessThan, false)]
    public void FractionalBounds_AreNotRoundedToIntegerMembers(
        int actual, double bound, IndexPredicateOperator op, bool expected)
    {
        var matcher = Matcher<IntegerEvent>(bound, op);

        Assert.True(matcher.HasIndex);
        Assert.Equal(expected, matcher.CouldMatch(new IntegerEvent(actual)));
    }

    [Theory]
    [InlineData(IndexPredicateOperator.LessThan, true)]
    [InlineData(IndexPredicateOperator.NotEquals, true)]
    [InlineData(IndexPredicateOperator.Equals, false)]
    public void DoubleBounds_PreservePrecisionForSingleMembers(IndexPredicateOperator op, bool expected)
    {
        var matcher = Matcher<SingleEvent>(1.00000001d, op);

        Assert.True(matcher.HasIndex);
        Assert.Equal(expected, matcher.CouldMatch(new SingleEvent(1f)));
    }

    [Theory]
    [InlineData(IndexPredicateOperator.Equals, true)]
    [InlineData(IndexPredicateOperator.NotEquals, false)]
    [InlineData(IndexPredicateOperator.GreaterThan, false)]
    [InlineData(IndexPredicateOperator.LessThanOrEqual, true)]
    public void LongMembers_ArePromotedWhenComparedWithDouble(IndexPredicateOperator op, bool expected)
    {
        var matcher = Matcher<LongEvent>(9007199254740992d, op);

        Assert.True(matcher.HasIndex);
        Assert.Equal(expected, matcher.CouldMatch(new LongEvent(9007199254740993L)));
    }

    [Fact]
    public void DecimalAndIntegralOperands_KeepExactPrecision()
    {
        var matcher = Matcher<DecimalEvent>(9007199254740993L, IndexPredicateOperator.Equals);

        Assert.True(matcher.HasIndex);
        Assert.True(matcher.CouldMatch(new DecimalEvent(9007199254740993m)));
        Assert.False(matcher.CouldMatch(new DecimalEvent(9007199254740992m)));
    }

    [Fact]
    public void FractionalDecimalBounds_AreNotRoundedToIntegerMembers()
    {
        var matcher = Matcher<IntegerEvent>(5.6m, IndexPredicateOperator.GreaterThan);

        Assert.True(matcher.HasIndex);
        Assert.True(matcher.CouldMatch(new IntegerEvent(6)));
        Assert.False(matcher.CouldMatch(new IntegerEvent(5)));
    }

    [Theory]
    [InlineData(IndexPredicateOperator.Equals, false)]
    [InlineData(IndexPredicateOperator.NotEquals, true)]
    [InlineData(IndexPredicateOperator.LessThan, true)]
    public void NumericPromotion_PreservesNullPassThrough(IndexPredicateOperator op, bool expected)
    {
        var matcher = Matcher<NullableEvent>(5.4d, op);

        Assert.True(matcher.HasIndex);
        Assert.Equal(expected, matcher.CouldMatch(new NullableEvent(null)));
    }

    private static EventIndexMatcher<TEvent> Matcher<TEvent>(object bound, IndexPredicateOperator op) =>
        EventIndexMatcher<TEvent>.Create([new IndexedPredicate("Value", op, bound, bound.GetType().Name)]);

    private sealed record IntegerEvent([property: EventIndexKey] int Value);
    private sealed record LongEvent([property: EventIndexKey] long Value);
    private sealed record SingleEvent([property: EventIndexKey] float Value);
    private sealed record DecimalEvent([property: EventIndexKey] decimal Value);
    private sealed record NullableEvent([property: EventIndexKey] int? Value);
}
