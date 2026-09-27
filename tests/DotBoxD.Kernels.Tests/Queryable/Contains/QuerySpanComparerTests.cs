using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QuerySpanComparerTests
{
    public static IEnumerable<object[]> EnumCases()
    {
        foreach (var kind in new[] { "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64" })
        {
            yield return [kind, false];
            yield return [kind, true];
        }
    }

    [Theory]
    [MemberData(nameof(EnumCases))]
    public void Ordinary_enum_array_membership_preserves_default_equality(string kind, bool nullable)
    {
        switch (kind)
        {
            case "SByte":
                CheckEnum<SignedByte>(nullable);
                break;
            case "Byte":
                CheckEnum<UnsignedByte>(nullable);
                break;
            case "Int16":
                CheckEnum<SignedShort>(nullable);
                break;
            case "UInt16":
                CheckEnum<UnsignedShort>(nullable);
                break;
            case "Int32":
                CheckEnum<SignedInt>(nullable);
                break;
            case "UInt32":
                CheckEnum<UnsignedInt>(nullable);
                break;
            case "Int64":
                CheckEnum<SignedLong>(nullable);
                break;
            case "UInt64":
                CheckEnum<UnsignedLong>(nullable);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Explicit_span_comparers_preserve_unsigned_and_null_values(bool mutable, bool defaultComparer)
    {
        UnsignedLong?[] values = [null, UnsignedLong.Max];
        var comparer = defaultComparer ? EqualityComparer<UnsignedLong?>.Default : null;
        Expression<Func<Sample<UnsignedLong>, bool>> predicate = mutable
            ? e => ((Span<UnsignedLong?>)values).Contains(e.Optional, comparer)
            : e => ((ReadOnlySpan<UnsignedLong?>)values).Contains(e.Optional, comparer);

        AssertParity(predicate, Enum.GetValues<UnsignedLong>().Select(value => new Sample<UnsignedLong>(value))
            .Append(new Sample<UnsignedLong>(null)));
    }

    [Theory]
    [InlineData("Null")]
    [InlineData("Default")]
    [InlineData("Ordinal")]
    public void Framework_string_comparers_preserve_ordinal_membership(string kind)
    {
        string?[] values = [null, "Alpha"];
#pragma warning disable MA0024 // Default and ordinal comparer identities are independently under test.
        IEqualityComparer<string?>? comparer = kind switch
        {
            "Null" => null,
            "Default" => EqualityComparer<string?>.Default,
            "Ordinal" => StringComparer.Ordinal,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
#pragma warning restore MA0024
        Expression<Func<TextSample, bool>>[] predicates =
        [
            e => ((ReadOnlySpan<string?>)values).Contains(e.Value, comparer),
            e => ((Span<string?>)values).Contains(e.Value, comparer)
        ];
        foreach (var predicate in predicates)
        {
            AssertParity(predicate, new string?[] { null, "Alpha", "alpha", "other" }.Select(value => new TextSample(value)));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Null_array_span_membership_is_empty(bool nullable, bool mutable)
    {
        UnsignedLong[] values = null!;
        UnsignedLong?[] optional = null!;
        Expression<Func<Sample<UnsignedLong>, bool>> predicate = nullable
            ? mutable ? e => ((Span<UnsignedLong?>)optional).Contains(e.Optional, null)
                : e => optional.Contains(e.Optional)
            : mutable ? e => ((Span<UnsignedLong>)values).Contains(e.Value, null)
                : e => values.Contains(e.Value);

        AssertParity(predicate, [new Sample<UnsignedLong>(null), new Sample<UnsignedLong>(UnsignedLong.Max)]);
    }

    private static void CheckEnum<TEnum>(bool nullable) where TEnum : struct, Enum
    {
        var all = Enum.GetValues<TEnum>();
        foreach (var selected in all)
        {
            TEnum[] values = [selected];
            TEnum?[] optional = [null, selected];
            Expression<Func<Sample<TEnum>, bool>> predicate = nullable
                ? e => optional.Contains(e.Optional)
                : e => values.Contains(e.Value);
            Assert.Equal(3, Assert.IsType<MethodCallExpression>(predicate.Body, exactMatch: false).Arguments.Count);
            AssertParity(predicate, all.Select(value => new Sample<TEnum>(value)).Append(new Sample<TEnum>(null)));
        }
    }

    private static void AssertParity<T>(Expression<Func<T, bool>> predicate, IEnumerable<T> samples)
    {
        var native = predicate.Compile();
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var sample in samples)
        {
            Assert.Equal(native(sample), QueryFilterEvaluator.Evaluate(filter, sample!, reader));
            Assert.Equal(native(sample), compiled(sample!));
        }
    }

    private sealed record Sample<TEnum>(TEnum? Optional) where TEnum : struct, Enum
    {
        public TEnum Value => Optional.GetValueOrDefault();
    }

    private sealed record TextSample(string? Value);
    private enum SignedByte : sbyte { Min = sbyte.MinValue, Zero = 0, Max = sbyte.MaxValue }
    private enum UnsignedByte : byte { Min = 0, Max = byte.MaxValue }
    private enum SignedShort : short { Min = short.MinValue, Zero = 0, Max = short.MaxValue }
    private enum UnsignedShort : ushort { Min = 0, Max = ushort.MaxValue }
    private enum SignedInt { Min = int.MinValue, Zero = 0, Max = int.MaxValue }
    private enum UnsignedInt : uint { Min = 0, Max = uint.MaxValue }
    private enum SignedLong : long { Min = long.MinValue, Zero = 0, Max = long.MaxValue }
    private enum UnsignedLong : ulong { Min = 0, BeforeMax = ulong.MaxValue - 1, Max = ulong.MaxValue }
}
