using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QuerySpanComparerCaptureTests
{
    [Fact]
    public void Collection_and_comparer_are_captured_once_in_argument_order()
    {
        var calls = new List<string>();
        string[] values = ["Alpha"];
        var source = new Source(() => { calls.Add("values"); return values; },
            () => { calls.Add("comparer"); return StringComparer.Ordinal; });

        var filter = ExpressionQueryTranslator.TranslateFilter<Sample>(
            e => ((ReadOnlySpan<string>)source.Values).Contains(e.Value, source.Comparer));
        values[0] = "other";

        Assert.Equal(["values", "comparer"], calls);
        Assert.Equal(QueryValue.FromString("Alpha"), Assert.Single(filter.Values));
        Assert.True(QueryFilterEvaluator.Evaluate(filter, new Sample("Alpha"), new MemberValueReader()));
        Assert.Equal(["values", "comparer"], calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Converted_spans_capture_the_selected_slice_once(bool mutable)
    {
        var source = new SpanSource(() => ["excluded", "included", "excluded"]);
#pragma warning disable MA0024 // Exercise the framework default comparer after a user span conversion.
        Expression<Func<Sample, bool>> predicate = mutable
            ? e => ((Span<string>)source).Contains(e.Value, EqualityComparer<string>.Default)
            : e => ((ReadOnlySpan<string>)source).Contains(e.Value, EqualityComparer<string>.Default);
#pragma warning restore MA0024

        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);

        Assert.Equal(1, source.Reads);
        Assert.Equal(QueryValue.FromString("included"), Assert.Single(filter.Values));
    }

    [Theory]
    [InlineData("Array")]
    [InlineData("Converted")]
    [InlineData("Mutable")]
    public void Comparer_getters_run_before_the_span_contents_are_copied(string kind)
    {
        string[] values = ["excluded", "original", "excluded"];
        var reads = 0;
        var source = new Source(() => values, () =>
        {
            reads++;
            values[1] = "updated";
            return StringComparer.Ordinal;
        });
        var spanSource = new SpanSource(() => values);
        Expression<Func<Sample, bool>> predicate = kind switch
        {
            "Array" => e => ((ReadOnlySpan<string>)source.Values).Contains(e.Value, source.Comparer),
            "Converted" => e => ((ReadOnlySpan<string>)spanSource).Contains(e.Value, source.Comparer),
            "Mutable" => e => ((Span<string>)spanSource).Contains(e.Value, source.Comparer),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.True(predicate.Compile()(new Sample("updated")));
        values[1] = "original";

        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);

        Assert.Equal(2, reads);
        var reader = new MemberValueReader();
        Assert.True(QueryFilterEvaluator.Evaluate(filter, new Sample("updated"), reader));
        Assert.False(QueryFilterEvaluator.Evaluate(filter, new Sample("original"), reader));
        Assert.True(QueryFilterCompiler.Compile(filter, reader)(new Sample("updated")));
    }

    [Theory]
    [InlineData("Collection", false)]
    [InlineData("Collection", true)]
    [InlineData("Comparer", false)]
    [InlineData("Comparer", true)]
    [InlineData("Conversion", false)]
    [InlineData("Conversion", true)]
    public void Capture_failures_and_cancellation_are_preserved(string stage, bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new InvalidOperationException("capture");
        var reads = 0;
        string[] ReadValues()
        {
            reads++;
            return stage == "Comparer" ? ["Alpha"] : throw failure;
        }

        IEqualityComparer<string>? ReadComparer()
        {
            reads++;
            throw failure;
        }

        var source = new Source(ReadValues, ReadComparer);
        var spanSource = new SpanSource(ReadValues);
        Expression<Func<Sample, bool>> predicate = stage == "Conversion"
            ? e => ((ReadOnlySpan<string>)spanSource).Contains(e.Value, null)
            : e => ((ReadOnlySpan<string>)source.Values).Contains(e.Value, source.Comparer);

        if (cancellation)
        {
            Assert.Same(failure, Assert.Throws<OperationCanceledException>(() => ExpressionQueryTranslator.TranslateFilter(predicate)));
        }
        else
        {
            Assert.Same(failure, Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate)).InnerException);
        }

        Assert.Equal(stage == "Comparer" ? 2 : 1, reads);
    }

    [Theory]
    [InlineData("IgnoreCase")]
    [InlineData("Culture")]
    [InlineData("Custom")]
    [InlineData("DifferentElementDefault")]
    public void Unsupported_comparers_are_rejected_without_invoking_them(string kind)
    {
        var custom = new RejectCallsComparer();
        IEqualityComparer<string> comparer = kind switch
        {
            "IgnoreCase" => StringComparer.OrdinalIgnoreCase,
            "Culture" => StringComparer.InvariantCulture,
            "Custom" => custom,
            "DifferentElementDefault" => EqualityComparer<object>.Default,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        string[] values = ["Alpha"];

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter<Sample>(
            e => ((ReadOnlySpan<string>)values).Contains(e.Value, comparer)));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, custom.Calls);
    }

    [Fact]
    public void Event_dependent_comparers_are_rejected_before_constant_capture()
    {
        var source = new Source(() => throw new InvalidOperationException("must not capture"), () => null);

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter<Sample>(
            e => ((ReadOnlySpan<string>)source.Values).Contains(e.Value, e.Comparer)));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Custom_three_argument_contains_is_not_a_framework_overload()
    {
        string[] values = ["Alpha"];
        Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter<Sample>(
            e => Contains(values, e.Value, null)));
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("ValueType")]
    [InlineData("Interface")]
    [InlineData("Enum")]
    public void Broad_element_types_do_not_merge_distinct_CLR_equality_domains(string kind)
    {
        switch (kind)
        {
            case "Object":
                AssertBroadTypeRejected<object>(1, 1L);
                break;
            case "ValueType":
                AssertBroadTypeRejected<ValueType>(1, 1L);
                break;
            case "Interface":
                AssertBroadTypeRejected<IComparable>(1, 1L);
                break;
            case "Enum":
                AssertBroadTypeRejected<Enum>(DayOfWeek.Monday, DateTimeKind.Utc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void AssertBroadTypeRejected<T>(T captured, T item)
    {
        T[] values = [captured];
        Expression<Func<BroadSample<T>, bool>> predicate = e => values.Contains(e.Value);
        Assert.False(predicate.Compile()(new BroadSample<T>(item)));
        Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));
    }

    private static bool Contains(string[] values, string item, IEqualityComparer<string>? comparer)
        => !Enumerable.Contains(values, item, comparer);

    private sealed record Sample(string Value)
    {
        public IEqualityComparer<string> Comparer => StringComparer.Ordinal;
    }

    private sealed record BroadSample<T>(T Value);

    private sealed class Source(Func<string[]> values, Func<IEqualityComparer<string>?> comparer)
    {
        public string[] Values => values();
        public IEqualityComparer<string>? Comparer => comparer();
    }

    private sealed class SpanSource(Func<string[]> values)
    {
        public int Reads { get; private set; }
        public static explicit operator ReadOnlySpan<string>(SpanSource source) => source.Read();
        public static explicit operator Span<string>(SpanSource source) => source.Read();
        private Span<string> Read()
        {
            Reads++;
            return values().AsSpan(1, 1);
        }
    }

    private sealed class RejectCallsComparer : IEqualityComparer<string>
    {
        public int Calls { get; private set; }
        public bool Equals(string? x, string? y)
        {
            Calls++;
            throw new InvalidOperationException("must not invoke");
        }

        public int GetHashCode(string value) => throw new InvalidOperationException("must not invoke");
    }
}
