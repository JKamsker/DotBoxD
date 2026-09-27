using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryReferenceComparisonTests
{
    public static IEnumerable<object[]> NonNullCases()
        => Cases(["object-string", "object-integer", "object-guid", "string", "interface"]);

    public static IEnumerable<object[]> NullCases() => Cases(["object", "string", "interface"]);

    private static IEnumerable<object[]> Cases(string[] kinds)
    {
        foreach (var kind in kinds)
        {
            foreach (var notEqual in new[] { false, true })
            {
                yield return [kind, notEqual, false];
                yield return [kind, notEqual, true];
            }
        }
    }

    [Theory]
    [MemberData(nameof(NonNullCases))]
    public void Non_null_reference_comparisons_are_rejected(string kind, bool notEqual, bool reversed)
    {
        switch (kind)
        {
            case "object-string":
                AssertRejected<object>(new string('x', 3), new string('x', 3), notEqual, reversed);
                break;
            case "object-integer":
                AssertRejected<object>(1, 1, notEqual, reversed);
                break;
            case "object-guid":
                AssertRejected<object>(Guid.Empty, Guid.Empty, notEqual, reversed);
                break;
            case "string":
                AssertRejected(new string('x', 3), new string('x', 3), notEqual, reversed);
                break;
            case "interface":
                AssertRejected<IComparable>(new string('x', 3), new string('x', 3), notEqual, reversed);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Authored_object_comparisons_are_rejected(bool notEqual)
    {
        object captured = new string('x', 3);
        Expression<Func<Sample<object>, bool>> predicate = notEqual
            ? e => e.Value != captured : e => e.Value == captured;
        Assert.Equal(notEqual, predicate.Compile()(new Sample<object>(new string('x', 3))));

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("reference", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(NullCases))]
    public void Captured_null_reference_comparisons_preserve_results_and_snapshot_capture(string kind, bool notEqual, bool reversed)
    {
        switch (kind)
        {
            case "object":
                AssertNull<object>([null, new object(), new string('x', 3), 1], notEqual, reversed);
                break;
            case "string":
                AssertNull<string>([null, new string('x', 3), "other"], notEqual, reversed);
                break;
            case "interface":
                AssertNull<IComparable>([null, new string('x', 3), 1], notEqual, reversed);
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
    public void Ordinary_string_operators_keep_value_equality(bool notEqual, bool nullCapture)
    {
        var captured = nullCapture ? null : new string('x', 3);
        Expression<Func<Sample<string>, bool>> predicate = notEqual
            ? e => e.Value != captured : e => e.Value == captured;
        Assert.NotNull(Assert.IsAssignableFrom<System.Linq.Expressions.BinaryExpression>(predicate.Body).Method);
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        var authored = predicate.Compile();
        foreach (var value in new[] { null, captured, new string('x', 3), "other" })
        {
            var input = new Sample<string>(value);
            Assert.Equal(authored(input), QueryFilterEvaluator.Evaluate(filter, input, reader));
            Assert.Equal(authored(input), compiled(input));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Captured_reference_getter_failures_are_preserved(bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new InvalidOperationException("getter");
        var source = new Capture<object>(() => throw failure);
        var predicate = Predicate(source, notEqual: false, reversed: false);

        var error = Record.Exception(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Same(failure, cancellation ? error : Assert.IsType<QueryTranslationException>(error).InnerException);
        Assert.Equal(1, source.Reads);
    }

    private static void AssertRejected<T>(T captured, T other, bool notEqual, bool reversed) where T : class
    {
        Assert.NotSame(captured, other);
        var source = new Capture<T>(() => captured);
        var predicate = Predicate(source, notEqual, reversed);
        var authored = predicate.Compile();
        Assert.Equal(notEqual, authored(new Sample<T>(other)));
        Assert.Equal(!notEqual, authored(new Sample<T>(captured)));
        var priorReads = source.Reads;

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("reference", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(priorReads + 1, source.Reads);
    }

    private static void AssertNull<T>(T?[] values, bool notEqual, bool reversed) where T : class
    {
        T? captured = null;
        var source = new Capture<T>(() => captured);
        var predicate = Predicate(source, notEqual, reversed);
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        Assert.Equal(1, source.Reads);
        captured = values[1];
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var value in values)
        {
            var input = new Sample<T>(value);
            var expected = notEqual ? value is not null : value is null;
            Assert.Equal(expected, QueryFilterEvaluator.Evaluate(filter, input, reader));
            Assert.Equal(expected, compiled(input));
        }

        Assert.Equal(1, source.Reads);
    }

    private static Expression<Func<Sample<T>, bool>> Predicate<T>(Capture<T> source, bool notEqual, bool reversed) where T : class
    {
        var parameter = LinqExpression.Parameter(typeof(Sample<T>), "e");
        LinqExpression member = LinqExpression.Property(parameter, nameof(Sample<T>.Value));
        LinqExpression constant = LinqExpression.Property(LinqExpression.Constant(source), nameof(Capture<T>.Value));
        var left = reversed ? constant : member;
        var right = reversed ? member : constant;
        var body = notEqual ? LinqExpression.ReferenceNotEqual(left, right) : LinqExpression.ReferenceEqual(left, right);
        Assert.Null(body.Method);
        return LinqExpression.Lambda<Func<Sample<T>, bool>>(body, parameter);
    }

    private sealed record Sample<T>(T? Value) where T : class;

    private sealed class Capture<T>(Func<T?> factory) where T : class
    {
        public int Reads { get; private set; }

        public T? Value
        {
            get
            {
                Reads++;
                return factory();
            }
        }
    }
}
