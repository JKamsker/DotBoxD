using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryMemberConversionMethodTests
{
    [Theory]
    [InlineData(nameof(Conversions.Invert), false)]
    [InlineData(nameof(Conversions.Invert), true)]
    [InlineData(nameof(Conversions.Widen), false)]
    [InlineData(nameof(Conversions.Widen), true)]
    [InlineData(nameof(Conversions.Decimal), false)]
    [InlineData(nameof(Conversions.Decimal), true)]
    public void Custom_methods_are_not_discarded_as_transparent_conversions(string method, bool checkedConversion)
    {
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var member = LinqExpression.Property(parameter, method == nameof(Conversions.Invert) ? nameof(Sample.Enabled) : nameof(Sample.Value));
        var converted = Convert(member, method, checkedConversion);
        var body = converted.Type == typeof(bool)
            ? (System.Linq.Expressions.Expression)converted
            : LinqExpression.Equal(converted, converted.Type == typeof(long) ? LinqExpression.Constant(1L) : LinqExpression.Constant(1m));
        var predicate = LinqExpression.Lambda<Func<Sample, bool>>(body, parameter);
        Assert.False(predicate.Compile()(new Sample()));

        AssertRejected(() => ExpressionQueryTranslator.TranslateFilter(predicate));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Lifted_and_reversed_conversions_are_rejected(bool checkedConversion, bool reversed)
    {
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var member = LinqExpression.Property(parameter, nameof(Sample.Optional));
        var converted = LinqExpression.MakeUnary(
            checkedConversion ? ExpressionType.ConvertChecked : ExpressionType.Convert,
            member, typeof(long?), typeof(Conversions).GetMethod(nameof(Conversions.Widen)));
        var constant = LinqExpression.Convert(LinqExpression.Constant(1L), typeof(long?));
        var body = reversed ? LinqExpression.Equal(constant, converted) : LinqExpression.Equal(converted, constant);
        var predicate = LinqExpression.Lambda<Func<Sample, bool>>(body, parameter);
        Assert.False(predicate.Compile()(new Sample()));

        AssertRejected(() => ExpressionQueryTranslator.TranslateFilter(predicate));
    }

    [Theory]
    [InlineData("Member", false)]
    [InlineData("Member", true)]
    [InlineData("Constructor", false)]
    [InlineData("Constructor", true)]
    [InlineData("Initializer", false)]
    [InlineData("Initializer", true)]
    [InlineData("Contains", false)]
    [InlineData("Contains", true)]
    public void Other_member_path_consumers_reject_custom_conversion_methods(string shape, bool checkedConversion)
    {
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var converted = Convert(LinqExpression.Property(parameter, nameof(Sample.Value)), nameof(Conversions.Widen), checkedConversion);
        if (shape == "Contains")
        {
            var call = LinqExpression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(long)],
                LinqExpression.Constant(new[] { 1L }), converted);
            var predicate = LinqExpression.Lambda<Func<Sample, bool>>(call, parameter);
            Assert.False(predicate.Compile()(new Sample()));
            AssertRejected(() => ExpressionQueryTranslator.TranslateFilter(predicate));
            return;
        }

        if (shape == "Member")
        {
            var projection = LinqExpression.Lambda<Func<Sample, long>>(converted, parameter);
            Assert.Equal(-1, projection.Compile()(new Sample()));
            AssertRejected(() => ExpressionQueryTranslator.TranslateProjection(projection));
            return;
        }

        var body = shape == "Constructor"
            ? (System.Linq.Expressions.Expression)LinqExpression.New(typeof(Projection).GetConstructor([typeof(long)])!, converted)
            : LinqExpression.MemberInit(LinqExpression.New(typeof(Projection)),
                LinqExpression.Bind(typeof(Projection).GetProperty(nameof(Projection.Value))!, converted));
        var constructed = LinqExpression.Lambda<Func<Sample, Projection>>(body, parameter);
        Assert.Equal(-1, constructed.Compile()(new Sample()).Value);
        AssertRejected(() => ExpressionQueryTranslator.TranslateProjection(constructed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_event_conversion_is_rejected_before_capturing_the_other_operand(bool cancellation)
    {
        var probe = new CaptureProbe(() => throw (cancellation ? new OperationCanceledException() : new InvalidOperationException("capture")));
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var converted = Convert(LinqExpression.Property(parameter, nameof(Sample.Value)), nameof(Conversions.Widen), checkedConversion: false);
        var captured = LinqExpression.Convert(LinqExpression.Property(LinqExpression.Constant(probe), nameof(CaptureProbe.Value)), typeof(long));
        var predicate = LinqExpression.Lambda<Func<Sample, bool>>(LinqExpression.Equal(converted, captured), parameter);

        AssertRejected(() => ExpressionQueryTranslator.TranslateFilter(predicate));
        Assert.Equal(0, probe.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constant_conversion_methods_still_run_once(bool checkedConversion)
    {
        var probe = new CaptureProbe(() => 1);
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var captured = Convert(LinqExpression.Property(LinqExpression.Constant(probe), nameof(CaptureProbe.Value)),
            nameof(Conversions.Widen), checkedConversion);
        var member = LinqExpression.Property(parameter, nameof(Sample.LongValue));
        var predicate = LinqExpression.Lambda<Func<Sample, bool>>(LinqExpression.Equal(member, captured), parameter);

        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);

        Assert.Equal(1, probe.Reads);
        Assert.Equal(-1, filter.Value!.Integer);
        Assert.Equal("p0", filter.Value.ParameterKey);
        var reader = new MemberValueReader();
        Assert.True(QueryFilterEvaluator.Evaluate(filter, new Sample(), reader));
        Assert.True(QueryFilterCompiler.Compile(filter, reader)(new Sample()));
        Assert.Equal(1, probe.Reads);
    }

    private static System.Linq.Expressions.UnaryExpression Convert(System.Linq.Expressions.Expression operand, string method, bool checkedConversion)
    {
        var conversion = typeof(Conversions).GetMethod(method)!;
        return LinqExpression.MakeUnary(checkedConversion ? ExpressionType.ConvertChecked : ExpressionType.Convert,
            operand, conversion.ReturnType, conversion);
    }

    private static void AssertRejected(Action translate)
    {
        var error = Assert.Throws<QueryTranslationException>(translate);
        Assert.Contains("cast", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Sample
    {
        public bool Enabled => true;
        public int Value => 1;
        public int? Optional => 1;
        public long LongValue => -1;
    }

    private sealed class Projection
    {
        public Projection() { }
        public Projection(long value) => Value = value;
        public long Value { get; set; }
    }

    private sealed class CaptureProbe(Func<int> read)
    {
        public int Reads { get; private set; }
        public int Value
        {
            get
            {
                Reads++;
                return read();
            }
        }
    }

    private static class Conversions
    {
        public static bool Invert(bool value) => !value;
        public static long Widen(int value) => -(long)value;
        public static decimal Decimal(int value) => -(decimal)value;
    }
}
