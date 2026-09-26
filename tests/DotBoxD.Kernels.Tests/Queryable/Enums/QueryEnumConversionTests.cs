using System.Globalization;
using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryEnumConversionTests
{
    public static IEnumerable<object[]> UnderlyingCases()
    {
        foreach (var kind in new[] { "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64" })
        {
            foreach (var nullable in new[] { false, true })
            {
                foreach (var checkedConversion in new[] { false, true })
                {
                    yield return [kind, nullable, checkedConversion];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnderlyingCases))]
    public void Exact_underlying_conversions_preserve_enum_values(string kind, bool nullable, bool checkedConversion)
    {
        switch (kind)
        {
            case "SByte":
                CheckUnderlying<SignedByte>(nullable, checkedConversion);
                break;
            case "Byte":
                CheckUnderlying<UnsignedByte>(nullable, checkedConversion);
                break;
            case "Int16":
                CheckUnderlying<SignedShort>(nullable, checkedConversion);
                break;
            case "UInt16":
                CheckUnderlying<UnsignedShort>(nullable, checkedConversion);
                break;
            case "Int32":
                CheckUnderlying<SignedInt>(nullable, checkedConversion);
                break;
            case "UInt32":
                CheckUnderlying<UnsignedInt>(nullable, checkedConversion);
                break;
            case "Int64":
                CheckUnderlying<SignedLong>(nullable, checkedConversion);
                break;
            case "UInt64":
                CheckUnderlying<UnsignedLong>(nullable, checkedConversion);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    [Theory]
    [InlineData("SByte")]
    [InlineData("Byte")]
    [InlineData("Int16")]
    [InlineData("UInt16")]
    [InlineData("Int32")]
    [InlineData("UInt32")]
    [InlineData("Int64")]
    [InlineData("UInt64")]
    public void Ordinary_CSharp_enum_comparisons_are_portable(string kind)
    {
        switch (kind)
        {
            case "SByte":
                CheckAuthored<SignedByte>(e => e.Value == SignedByte.Max, e => e.Optional != SignedByte.Min);
                break;
            case "Byte":
                CheckAuthored<UnsignedByte>(e => e.Value == UnsignedByte.Max, e => e.Optional != UnsignedByte.Min);
                break;
            case "Int16":
                CheckAuthored<SignedShort>(e => e.Value == SignedShort.Max, e => e.Optional != SignedShort.Min);
                break;
            case "UInt16":
                CheckAuthored<UnsignedShort>(e => e.Value == UnsignedShort.Max, e => e.Optional != UnsignedShort.Min);
                break;
            case "Int32":
                CheckAuthored<SignedInt>(e => e.Value == SignedInt.Max, e => e.Optional != SignedInt.Min);
                break;
            case "UInt32":
                CheckAuthored<UnsignedInt>(e => e.Value == UnsignedInt.Max, e => e.Optional != UnsignedInt.Min);
                break;
            case "Int64":
                CheckAuthored<SignedLong>(e => e.Value == SignedLong.Max, e => e.Optional != SignedLong.Min);
                break;
            case "UInt64":
                CheckAuthored<UnsignedLong>(e => e.Value == UnsignedLong.Max, e => e.Optional != UnsignedLong.Min);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void CheckUnderlying<TEnum>(bool nullable, bool checkedConversion) where TEnum : struct, Enum
    {
        var underlying = Enum.GetUnderlyingType(typeof(TEnum));
        var target = nullable ? typeof(Nullable<>).MakeGenericType(underlying) : underlying;
        var parameter = LinqExpression.Parameter(typeof(Sample<TEnum>), "e");
        var member = LinqExpression.Property(parameter, nullable ? nameof(Sample<TEnum>.Optional) : nameof(Sample<TEnum>.Value));
        var converted = LinqExpression.MakeUnary(checkedConversion ? ExpressionType.ConvertChecked : ExpressionType.Convert, member, target);
        foreach (var selected in Enum.GetValues<TEnum>())
        {
            System.Linq.Expressions.Expression constant = LinqExpression.Constant(Convert.ChangeType(selected, underlying, CultureInfo.InvariantCulture), underlying);
            if (nullable)
            {
                constant = LinqExpression.Convert(constant, target);
            }

            var predicate = LinqExpression.Lambda<Func<Sample<TEnum>, bool>>(LinqExpression.Equal(converted, constant), parameter);
            CheckAuthored(predicate);
        }
    }

    private static void CheckAuthored<TEnum>(params Expression<Func<Sample<TEnum>, bool>>[] predicates) where TEnum : struct, Enum
    {
        var samples = Enum.GetValues<TEnum>().Select(value => new Sample<TEnum>(value)).Append(new Sample<TEnum>(null));
        foreach (var predicate in predicates)
        {
            var authored = predicate.Compile();
            var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
            var reader = new MemberValueReader();
            var compiled = QueryFilterCompiler.Compile(filter, reader);
            foreach (var sample in samples)
            {
                Assert.Equal(authored(sample), QueryFilterEvaluator.Evaluate(filter, sample, reader));
                Assert.Equal(authored(sample), compiled(sample));
            }
        }
    }

    private sealed record Sample<TEnum>(TEnum? Optional) where TEnum : struct, Enum
    {
        public TEnum Value => Optional.GetValueOrDefault();
    }

    private enum SignedByte : sbyte { Min = sbyte.MinValue, Zero = 0, Max = sbyte.MaxValue }
    private enum UnsignedByte : byte { Min = 0, Max = byte.MaxValue }
    private enum SignedShort : short { Min = short.MinValue, Zero = 0, Max = short.MaxValue }
    private enum UnsignedShort : ushort { Min = 0, Max = ushort.MaxValue }
    private enum SignedInt { Min = int.MinValue, Zero = 0, Max = int.MaxValue }
    private enum UnsignedInt : uint { Min = 0, Max = uint.MaxValue }
    private enum SignedLong : long { Min = long.MinValue, Zero = 0, Max = long.MaxValue }
    private enum UnsignedLong : ulong { Min = 0, Max = ulong.MaxValue }
}
