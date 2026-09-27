using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryConstantConversionTests
{
    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(true, 0, 1)]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(false, 2, 16777216)]
    [InlineData(true, 2, 16777216)]
    [InlineData(false, 3, 1)]
    [InlineData(true, 3, 1)]
    public void Constant_conversions_preserve_numeric_results(bool projection, int conversion, int expected)
    {
        var operand = conversion switch
        {
            0 => LinqExpression.Convert(LinqExpression.Constant(1.75), typeof(int)),
            1 => LinqExpression.Convert(LinqExpression.Constant(4294967297L), typeof(int)),
            2 => LinqExpression.Convert(
                LinqExpression.Convert(LinqExpression.Constant(16777217), typeof(float)), typeof(int)),
            _ => LinqExpression.Convert(
                LinqExpression.Convert(LinqExpression.Constant(257), typeof(byte)), typeof(int))
        };

        Assert.Equal(expected, LinqExpression.Lambda<Func<int>>(operand).Compile()());
        AssertConstant(projection, operand, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Checked_constant_conversion_reports_overflow(bool projection)
    {
        var operand = LinqExpression.ConvertChecked(LinqExpression.Constant(long.MaxValue), typeof(int));

        var exception = Assert.Throws<QueryTranslationException>(() => TranslateConstant(projection, operand));

        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void User_defined_constant_conversion_is_evaluated_once(bool projection)
    {
        var source = new ConvertedConstant();
        var operand = LinqExpression.Convert(LinqExpression.Constant(source), typeof(int));

        AssertConstant(projection, operand, 42);

        Assert.Equal(1, source.Reads);
    }

    private static void AssertConstant(bool projection, LinqExpression operand, int expected)
    {
        var constant = TranslateConstant(projection, operand);
        Assert.Equal(QueryValueKind.Integer, constant.Kind);
        Assert.Equal(expected, constant.Integer);
    }

    private static QueryValue TranslateConstant(bool projection, LinqExpression operand)
    {
        var parameter = LinqExpression.Parameter(typeof(AttackTestEvent), "e");
        if (projection)
        {
            var construction = LinqExpression.New(
                typeof(AttackNotice).GetConstructor([typeof(string), typeof(string), typeof(int)])!,
                LinqExpression.Constant("alice"), LinqExpression.Constant("bob"), operand);
            var expression = LinqExpression.Lambda<Func<AttackTestEvent, AttackNotice>>(construction, parameter);
            return ExpressionQueryTranslator.TranslateProjection(expression).Fields[2].Constant!;
        }

        var comparison = LinqExpression.Equal(LinqExpression.Property(parameter, nameof(AttackTestEvent.Damage)), operand);
        var predicate = LinqExpression.Lambda<Func<AttackTestEvent, bool>>(comparison, parameter);
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        return filter.Value!;
    }

    private sealed class ConvertedConstant
    {
        public int Reads { get; private set; }

        public static implicit operator int(ConvertedConstant value)
        {
            value.Reads++;
            return 42;
        }
    }
}
