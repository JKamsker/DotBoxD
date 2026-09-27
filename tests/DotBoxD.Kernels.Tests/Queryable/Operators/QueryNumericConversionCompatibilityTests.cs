using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryNumericConversionCompatibilityTests
{
    [Theory]
    [InlineData("Byte", false, false)]
    [InlineData("Byte", false, true)]
    [InlineData("Byte", true, false)]
    [InlineData("Byte", true, true)]
    [InlineData("SByte", false, false)]
    [InlineData("SByte", false, true)]
    [InlineData("SByte", true, false)]
    [InlineData("SByte", true, true)]
    [InlineData("Int16", false, false)]
    [InlineData("Int16", false, true)]
    [InlineData("Int16", true, false)]
    [InlineData("Int16", true, true)]
    [InlineData("UInt16", false, false)]
    [InlineData("UInt16", false, true)]
    [InlineData("UInt16", true, false)]
    [InlineData("UInt16", true, true)]
    [InlineData("Int32", false, false)]
    [InlineData("Int32", false, true)]
    [InlineData("Int32", true, false)]
    [InlineData("Int32", true, true)]
    [InlineData("UInt32", false, false)]
    [InlineData("UInt32", false, true)]
    [InlineData("UInt32", true, false)]
    [InlineData("UInt32", true, true)]
    [InlineData("Int64", false, false)]
    [InlineData("Int64", false, true)]
    [InlineData("Int64", true, false)]
    [InlineData("Int64", true, true)]
    [InlineData("UInt64", false, false)]
    [InlineData("UInt64", false, true)]
    [InlineData("UInt64", true, false)]
    [InlineData("UInt64", true, true)]
    public void Framework_decimal_widening_preserves_filter_and_projection_paths(string name, bool nullable, bool checkedConversion)
    {
        var field = nullable ? name + "Optional" : name;
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var member = LinqExpression.Property(parameter, field);
        var target = nullable ? typeof(decimal?) : typeof(decimal);
        var converted = LinqExpression.MakeUnary(checkedConversion ? ExpressionType.ConvertChecked : ExpressionType.Convert, member, target);
        Assert.Equal(typeof(decimal), converted.Method!.DeclaringType);
        var constant = nullable
            ? (System.Linq.Expressions.Expression)LinqExpression.Convert(LinqExpression.Constant(1m), target)
            : LinqExpression.Constant(1m);
        var predicate = LinqExpression.Lambda<Func<Sample, bool>>(LinqExpression.Equal(converted, constant), parameter);

        AssertFilterParity(predicate);

        var projection = nullable
            ? ExpressionQueryTranslator.TranslateProjection(LinqExpression.Lambda<Func<Sample, decimal?>>(converted, parameter))
            : ExpressionQueryTranslator.TranslateProjection(LinqExpression.Lambda<Func<Sample, decimal>>(converted, parameter));
        Assert.Equal(QueryProjectionKind.Member, projection.Kind);
        Assert.Equal(field, projection.Path);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Intrinsic_widening_still_preserves_comparison_results(bool nullable, bool checkedConversion)
    {
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var member = LinqExpression.Property(parameter, nullable ? nameof(Sample.Int32Optional) : nameof(Sample.Int32));
        var target = nullable ? typeof(long?) : typeof(long);
        var converted = LinqExpression.MakeUnary(checkedConversion ? ExpressionType.ConvertChecked : ExpressionType.Convert, member, target);
        Assert.Null(converted.Method);
        var constant = nullable
            ? (System.Linq.Expressions.Expression)LinqExpression.Convert(LinqExpression.Constant(1L), target)
            : LinqExpression.Constant(1L);
        var predicate = LinqExpression.Lambda<Func<Sample, bool>>(LinqExpression.Equal(converted, constant), parameter);

        AssertFilterParity(predicate);
    }

    private static void AssertFilterParity(Expression<Func<Sample, bool>> predicate)
    {
        var authored = predicate.Compile();
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var value in new int?[] { null, 0, 1, 2 })
        {
            var sample = new Sample(value);
            Assert.Equal(authored(sample), QueryFilterEvaluator.Evaluate(filter, sample, reader));
            Assert.Equal(authored(sample), compiled(sample));
        }
    }

    private sealed class Sample(int? value)
    {
        public byte Byte => (byte)value.GetValueOrDefault();
        public sbyte SByte => (sbyte)value.GetValueOrDefault();
        public short Int16 => (short)value.GetValueOrDefault();
        public ushort UInt16 => (ushort)value.GetValueOrDefault();
        public int Int32 => value.GetValueOrDefault();
        public uint UInt32 => (uint)value.GetValueOrDefault();
        public long Int64 => value.GetValueOrDefault();
        public ulong UInt64 => (ulong)value.GetValueOrDefault();
        public byte? ByteOptional => (byte?)value;
        public sbyte? SByteOptional => (sbyte?)value;
        public short? Int16Optional => (short?)value;
        public ushort? UInt16Optional => (ushort?)value;
        public int? Int32Optional => value;
        public uint? UInt32Optional => (uint?)value;
        public long? Int64Optional => value;
        public ulong? UInt64Optional => (ulong?)value;
    }
}
