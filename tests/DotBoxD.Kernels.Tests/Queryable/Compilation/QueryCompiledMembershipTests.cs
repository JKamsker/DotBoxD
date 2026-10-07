using System.Collections.ObjectModel;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryCompiledMembershipTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(256)]
    public void Lookup_matches_linear_numeric_equality_in_both_domains(int count)
    {
        QueryValue[] seeds = [QueryValue.FromInteger(long.MinValue), QueryValue.FromInteger(9007199254740993),
            QueryValue.FromUnsignedInteger(ulong.MaxValue), QueryValue.FromDecimal(1.100m),
            QueryValue.FromInteger(0), QueryValue.FromInteger(7), QueryValue.FromDecimal(decimal.MaxValue)];
        var values = Enumerable.Range(0, count).Select(i => seeds[i % seeds.Length]).ToArray();
        var filter = QueryFilter.In("Value", values);
        var reader = new MemberValueReader(typeof(Sample));
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        object?[] actuals = [null, true, "7", (sbyte)7, (byte)7, (short)7, (ushort)7, 7, 7U, 7L, 7UL, 7m,
            1.1m, 1.1f, 1.1d, ulong.MaxValue, long.MinValue, 9007199254740992L, 9007199254740993L,
            9007199254740992d, double.NaN, double.PositiveInfinity, decimal.MaxValue, Flags.Max, -1];
        foreach (var actual in actuals)
        {
            var sample = new Sample(actual);
            Assert.Equal(QueryFilterEvaluator.Evaluate(filter, sample, reader), compiled(sample));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void String_lookups_preserve_ordinal_comparers_and_incomparable_values(bool ignoreCase)
    {
        var values = new[] { "a", "A", "I", "İ", "ı", "ß", "SS", "value" }.Select(QueryValue.FromString).ToArray();
        AssertEquivalent(QueryFilter.In("Value", values, ignoreCase), ["a", "A", "i", "İ", "ı", "ss", "ß", "VALUE", "absent", null, 7]);
    }

    [Fact]
    public void Guid_duplicates_nulls_mixed_literals_and_timestamp_fallbacks_keep_linear_semantics()
    {
        var guid = Guid.NewGuid();
        AssertEquivalent(QueryFilter.In("Value", Enumerable.Repeat(QueryValue.FromGuid(guid), 8).ToArray()), [guid, Guid.Empty, null, guid.ToString()]);
        QueryValue[] mixed = [QueryValue.Null, QueryValue.FromInteger(7), QueryValue.FromNumber(7), QueryValue.FromString("7"),
            QueryValue.FromGuid(guid), QueryValue.FromBoolean(true), QueryValue.FromDecimal(7), QueryValue.FromUnsignedInteger(7)];
        AssertEquivalent(QueryFilter.In("Value", mixed), [7, 7m, 7d, "7", guid, true, null, false]);
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        AssertEquivalent(QueryFilter.In("Value", Enumerable.Repeat(QueryValue.FromTimestamp(instant), 8).ToArray()),
            [instant, instant.ToOffset(TimeSpan.FromHours(1)), instant.UtcDateTime, new DateOnly(2026, 1, 1), null]);
    }

    [Fact]
    public void Public_compile_keeps_mutable_candidate_collections_live()
    {
        var values = Enumerable.Repeat(QueryValue.FromInteger(7), 8).ToList();
        var reader = new MemberValueReader(typeof(Sample));
        foreach (var candidates in new IReadOnlyList<QueryValue>[] { values, new ReadOnlyCollection<QueryValue>(values) })
        {
            var filter = QueryFilter.In("Value", []) with { Values = candidates };
            var compiled = QueryFilterCompiler.Compile(filter, reader);
            Assert.False(compiled(new Sample(8)));
            values[0] = QueryValue.FromInteger(8);
            Assert.True(compiled(new Sample(8)));
            values[0] = QueryValue.FromInteger(7);
        }
    }

    [Fact]
    public void Factory_snapshot_is_independent_of_the_original_array()
    {
        var values = Enumerable.Repeat(QueryValue.FromInteger(7), 8).ToArray();
        var filter = QueryFilter.In("Value", values);
        var compiled = QueryFilterCompiler.Compile(filter, new MemberValueReader(typeof(Sample)));
        values[0] = QueryValue.FromInteger(8);
        Assert.True(compiled(new Sample(7)));
        Assert.False(compiled(new Sample(8)));
    }

    private static void AssertEquivalent(QueryFilter filter, object?[] actuals)
    {
        foreach (var reader in new[] { new MemberValueReader(typeof(Sample)), new MemberValueReader() })
        {
            var compiled = QueryFilterCompiler.Compile(filter, reader);
            foreach (var actual in actuals)
            {
                var sample = new Sample(actual);
                Assert.Equal(QueryFilterEvaluator.Evaluate(filter, sample, reader), compiled(sample));
            }
        }
    }

    private sealed record Sample(object? Value);
    private enum Flags : ulong { Max = ulong.MaxValue }
}
