using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryEnumConversionCompatibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Captured_unsigned_extremes_remain_exact_in_either_operand_order(bool nullable, bool reversed)
    {
        var selected = UnsignedValue.Max;
        Expression<Func<UnsignedSample, bool>> predicate = nullable
            ? reversed ? e => selected == e.Optional : e => e.Optional == selected
            : reversed ? e => selected == e.Value : e => e.Value == selected;

        var filter = AssertUnsignedParity(predicate);

        Assert.Equal(QueryValueKind.UnsignedInteger, filter.Value!.Kind);
        Assert.Equal(ulong.MaxValue, filter.Value.UnsignedInteger);
        Assert.Equal("p0", filter.Value.ParameterKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Underlying_value_projections_preserve_the_member_path(bool nullable)
    {
        var projection = nullable
            ? ExpressionQueryTranslator.TranslateProjection<SignedSample, int?>(e => (int?)e.Optional)
            : ExpressionQueryTranslator.TranslateProjection<SignedSample, int>(e => (int)e.Value);

        Assert.Equal(QueryProjectionKind.Member, projection.Kind);
        Assert.Equal(nullable ? nameof(SignedSample.Optional) : nameof(SignedSample.Value), projection.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enum_equality_routes_unsigned_values_without_rounding(bool nullable)
    {
        var selected = UnsignedValue.Max;
        Expression<Func<UnsignedSample, bool>> predicate = nullable ? e => e.Optional == selected : e => e.Value == selected;
        var host = new EventQueryHost();
        var received = new List<UnsignedValue>();
        using var handle = await host.Query<UnsignedSample>().Where(predicate).SubscribeAsync((value, _) =>
        {
            received.Add(value.Value);
            return ValueTask.CompletedTask;
        });
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        foreach (var value in new UnsignedValue?[] { null, UnsignedValue.Zero, UnsignedValue.BeforeMax, UnsignedValue.Max })
        {
            await host.PublishAsync(new UnsignedSample(value), context);
        }

        Assert.Equal([UnsignedValue.Max], received);
        Assert.True(handle.Plan.IsRoutable);
        Assert.Equal(4, handle.EventsObserved);
        Assert.Equal(1, handle.FilterEvaluations);
        Assert.Equal(1, handle.Dispatches);
    }

    [Fact]
    public void Enum_casts_with_the_same_underlying_type_preserve_values()
        => AssertSignedParity(e => (OtherSignedValue)e.Value == OtherSignedValue.Min);

    [Fact]
    public void Integral_to_enum_casts_preserve_the_underlying_value()
        => AssertSignedParity(e => (SignedValue)e.Number == SignedValue.Max);

    [Theory]
    [InlineData("Narrow")]
    [InlineData("UnsignedToSigned")]
    [InlineData("SignedToUnsigned")]
    [InlineData("Floating")]
    [InlineData("NullableUnwrap")]
    public void Lossy_or_null_changing_enum_conversions_remain_unsupported(string kind)
    {
#pragma warning disable CS8629 // The nullable unwrap is intentionally rejected before runtime.
        Expression<Func<SignedSample, bool>> predicate = kind switch
        {
            "Narrow" => e => (int)e.LongValue == 1,
            "UnsignedToSigned" => e => (long)e.Unsigned == 1,
            "SignedToUnsigned" => e => (uint)e.Value == 1,
            "Floating" => e => (double)e.LongValue == 1,
            "NullableUnwrap" => e => (int)e.Optional == 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
#pragma warning restore CS8629

        AssertRejected(() => ExpressionQueryTranslator.TranslateFilter(predicate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_enum_conversion_methods_are_not_discarded(bool checkedConversion)
    {
        var parameter = LinqExpression.Parameter(typeof(SignedSample), "e");
        var member = LinqExpression.Property(parameter, nameof(SignedSample.Value));
        var method = typeof(QueryEnumConversionCompatibilityTests).GetMethod(nameof(ChangeValue),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var converted = LinqExpression.MakeUnary(checkedConversion ? ExpressionType.ConvertChecked : ExpressionType.Convert,
            member, typeof(int), method);
        var body = LinqExpression.Equal(converted, LinqExpression.Constant(1));
        var predicate = LinqExpression.Lambda<Func<SignedSample, bool>>(body, parameter);

        AssertRejected(() => ExpressionQueryTranslator.TranslateFilter(predicate));
    }

    private static int ChangeValue(SignedValue value) => (int)value ^ 1;

    private static void AssertRejected(Action translate)
    {
        var error = Assert.Throws<QueryTranslationException>(translate);
        Assert.Contains("cast", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static QueryFilter AssertUnsignedParity(Expression<Func<UnsignedSample, bool>> predicate)
    {
        var authored = predicate.Compile();
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var value in new UnsignedValue?[] { null, UnsignedValue.Zero, UnsignedValue.BeforeMax, UnsignedValue.Max })
        {
            var sample = new UnsignedSample(value);
            Assert.Equal(authored(sample), QueryFilterEvaluator.Evaluate(filter, sample, reader));
            Assert.Equal(authored(sample), compiled(sample));
        }

        return filter;
    }

    private static void AssertSignedParity(Expression<Func<SignedSample, bool>> predicate)
    {
        var authored = predicate.Compile();
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var value in new SignedValue?[] { null, SignedValue.Min, SignedValue.Zero, SignedValue.Max })
        {
            var sample = new SignedSample(value);
            Assert.Equal(authored(sample), QueryFilterEvaluator.Evaluate(filter, sample, reader));
            Assert.Equal(authored(sample), compiled(sample));
        }
    }

    private sealed record SignedSample(SignedValue? Optional)
    {
        public SignedValue Value => Optional.GetValueOrDefault();
        public int Number => (int)Value;
        public LongValue LongValue => LongValue.Max;
        public UnsignedValue Unsigned => UnsignedValue.Max;
    }

    private sealed record UnsignedSample(UnsignedValue? Optional)
    {
        public UnsignedValue Value => Optional.GetValueOrDefault();
    }

    private enum SignedValue { Min = int.MinValue, Zero = 0, Max = int.MaxValue }
    private enum OtherSignedValue { Min = int.MinValue, Zero = 0, Max = int.MaxValue }
    private enum LongValue : long { Max = long.MaxValue }
    private enum UnsignedValue : ulong { Zero = 0, BeforeMax = ulong.MaxValue - 1, Max = ulong.MaxValue }
}
