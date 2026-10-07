using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryCompiledMemberTests
{
    [Fact]
    public void Typed_scalar_paths_preserve_all_comparison_domains()
    {
        Check(new Sample<sbyte>(sbyte.MinValue));
        Check(new Sample<byte>(byte.MaxValue));
        Check(new Sample<short>(short.MinValue));
        Check(new Sample<ushort>(ushort.MaxValue));
        Check(new Sample<int>(int.MaxValue));
        Check(new Sample<uint>(uint.MaxValue));
        Check(new Sample<long>(9007199254740993));
        Check(new Sample<ulong>(ulong.MaxValue));
        Check(new Sample<decimal>(1.100m));
        Check(new Sample<decimal>(decimal.MaxValue));
        Check(new Sample<float>(0.1f));
        Check(new Sample<double>(double.NaN));
        Check(new Sample<double>(double.PositiveInfinity));
        Check(new Sample<double>(-0.0));
        Check(new Sample<Flags>(Flags.Max));
        Check(new Sample<bool>(true));
        Check(new Sample<string?>(null));
        Check(new Sample<string>("VALUE"));
        Check(new Sample<Guid>(Guid.Empty));
        Check(new Sample<int?>(7));
        Check(new Sample<int?>(null));
        Check(new Sample<Flags?>(Flags.Max));
        Check(new Sample<Flags?>(null));
        Check(new Sample<DateTime>(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)));
        Check(new Sample<DateTimeOffset>(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.FromHours(1))));
        Check(new Sample<DateOnly>(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Nested_reads_short_circuit_nulls_and_run_each_getter_once()
    {
        var filter = QueryFilter.Compare("Nested.Value", QueryComparisonOperator.NotEqual, QueryValue.FromInteger(7));
        var reader = new MemberValueReader(typeof(NestedRoot));
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        var root = new NestedRoot();
        Assert.True(compiled(root));
        Assert.Equal(1, root.Reads);
        Assert.Equal(1, root.Nested!.Reads);
        root = new NestedRoot { NestedValue = null };
        Assert.True(compiled(root));
        Assert.Equal(1, root.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Throwing_getters_remain_null_for_equality_and_negation(bool nested)
    {
        var reader = new MemberValueReader(typeof(FailingRoot));
        var root = new FailingRoot();
        var path = nested ? "Nested.Value" : "Value";
        foreach (var expected in new[] { QueryValue.Null, QueryValue.FromInteger(7) })
        {
            foreach (var op in Enum.GetValues<QueryComparisonOperator>())
            {
                AssertEquivalent(root, reader, QueryFilter.Compare(path, op, expected));
            }
        }
    }

    [Fact]
    public void Fields_hidden_members_virtual_dispatch_and_explicit_interfaces_keep_declared_identity()
    {
        var value = new Derived();
        var expected = QueryValue.FromInteger(7);
        AssertEquivalent(value, new MemberValueReader(typeof(Base)), QueryFilter.Compare("Value", QueryComparisonOperator.Equal, expected));
        AssertEquivalent(value, new MemberValueReader(typeof(Base)), QueryFilter.Compare("Virtual", QueryComparisonOperator.Equal, expected));
        AssertEquivalent(value, new MemberValueReader(typeof(Derived)), QueryFilter.Compare("Field", QueryComparisonOperator.Equal, expected));
        AssertEquivalent(value, new MemberValueReader(typeof(IBase)), QueryFilter.Compare("Value", QueryComparisonOperator.Equal, expected));
        AssertEquivalent(value, new MemberValueReader(), QueryFilter.Compare("Value", QueryComparisonOperator.Equal, expected));
        AssertEquivalent(new Sample<int>(7), new MemberValueReader(typeof(Sample<int>)),
            QueryFilter.Compare("Value", QueryComparisonOperator.Equal, expected));
    }

    [Fact]
    public void Invalid_targets_and_paths_keep_reader_errors()
    {
        var reader = new MemberValueReader(typeof(Base));
        var filter = QueryFilter.Compare("Value", QueryComparisonOperator.Equal, QueryValue.FromInteger(7));
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        Assert.Equal("target", Assert.Throws<ArgumentNullException>(() => compiled(null!)).ParamName);
        Assert.Equal("target", Assert.Throws<ArgumentException>(() => compiled(new object())).ParamName);
        compiled = QueryFilterCompiler.Compile(filter with { Field = "Missing" }, reader);
        Assert.Throws<InvalidOperationException>(() => compiled(new Base()));
    }

    [Fact]
    public void Nullable_intermediate_keeps_reflection_behavior()
    {
        foreach (var root in new[] { new Sample<int?>(7), new Sample<int?>(null) })
        {
            AssertEquivalent(root, new MemberValueReader(typeof(Sample<int?>)),
                QueryFilter.Compare("Value.Value", QueryComparisonOperator.Equal, QueryValue.FromInteger(7)));
        }
    }

    [Fact]
    public void Mutating_struct_getters_keep_boxed_receiver_state()
    {
        var filter = QueryFilter.Compare("Value", QueryComparisonOperator.Equal, QueryValue.FromInteger(1));
        var reader = new MemberValueReader(typeof(Mutating));
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        object interpretedValue = new Mutating();
        object compiledValue = new Mutating();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(QueryFilterEvaluator.Evaluate(filter, interpretedValue, reader), compiled(compiledValue));
            Assert.Equal(((Mutating)interpretedValue).Reads, ((Mutating)compiledValue).Reads);
        }
    }

    private static void Check<T>(Sample<T> value)
    {
        var reader = new MemberValueReader(typeof(Sample<T>));
        QueryValue[] literals = [QueryValue.Null, QueryValue.FromBoolean(true), QueryValue.FromString("value"),
            QueryValue.FromGuid(Guid.Empty), QueryValue.FromTimestamp(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            QueryValue.FromInteger(7), QueryValue.FromInteger(long.MinValue), QueryValue.FromUnsignedInteger(ulong.MaxValue),
            QueryValue.FromInteger(9007199254740993), QueryValue.FromDecimal(1.1m), QueryValue.FromDecimal(decimal.MaxValue),
            QueryValue.FromNumber(0.1), QueryValue.FromNumber(9007199254740992)];
        foreach (var literal in literals)
        {
            foreach (var op in Enum.GetValues<QueryComparisonOperator>())
            {
                AssertEquivalent(value, reader, QueryFilter.Compare("Value", op, literal, ignoreCase: true));
            }
        }
    }

    private static void AssertEquivalent(object value, MemberValueReader reader, QueryFilter filter)
    {
        var expected = QueryFilterEvaluator.Evaluate(filter, value, reader);
        Assert.Equal(expected, QueryFilterCompiler.Compile(filter, reader)(value));
    }

    private readonly record struct Sample<T>(T Value);
    private struct Mutating
    {
        public int Reads;
        public int Value => ++Reads;
    }
    private enum Flags : ulong { Max = ulong.MaxValue }
    private interface IBase { int Value { get; } }
    private class Base
    {
        public int Value => 7;
        public virtual int Virtual => 99;
    }

    private sealed class Derived : Base, IBase
    {
        public new int Value => 99;
        public override int Virtual => 7;
        public readonly int Field = 7;
        int IBase.Value => 7;
    }

    private sealed class NestedRoot
    {
        public Counter? NestedValue { get; init; } = new();
        public int Reads { get; private set; }
        public Counter? Nested { get { Reads++; return NestedValue; } }
    }

    private sealed class Counter
    {
        public int Reads { get; private set; }
        public int Value { get { Reads++; return 8; } }
    }

    private sealed class FailingRoot
    {
        public int Value => throw new IOException("Getter failure");
        public Counter Nested => throw new OperationCanceledException();
    }
}
