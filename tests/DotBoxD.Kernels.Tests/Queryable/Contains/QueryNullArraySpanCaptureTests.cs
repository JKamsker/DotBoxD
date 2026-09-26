using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryNullArraySpanCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var strings in new[] { false, true })
        {
            foreach (var readOnly in new[] { false, true })
            {
                foreach (var source in new[] { "null", "constant-null", "empty", "values", "property-null", "property-empty", "property-values" })
                {
                    yield return [strings, readOnly, source];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Array_to_span_capture_preserves_null_and_nonnull_membership(bool strings, bool readOnly, string source)
    {
        if (strings)
        {
            AssertMembership("alice", "ALICE", readOnly, source);
        }
        else
        {
            AssertMembership(1, 2, readOnly, source);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Array_getter_failures_are_preserved(bool readOnly, bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new InvalidOperationException("array getter");
        var source = new ArraySource<int>(() => throw failure);
        Expression<Func<Sample<int>, bool>> predicate = readOnly
            ? e => ((ReadOnlySpan<int>)source.Values).Contains(e.Value)
            : e => ((Span<int>)source.Values).Contains(e.Value);

        var error = Record.Exception(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Same(failure, cancellation ? error : Assert.IsType<QueryTranslationException>(error).InnerException);
        Assert.Equal(1, source.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Null_collection_membership_is_still_rejected(bool staticContains)
    {
        ICollection<int> values = null!;
        Expression<Func<Sample<int>, bool>> predicate = staticContains
            ? e => Enumerable.Contains(values, e.Value) : e => values.Contains(e.Value);
        var nativeError = Record.Exception(() => predicate.Compile()(new Sample<int>(1)));
        if (staticContains)
        {
            Assert.IsType<ArgumentNullException>(nativeError);
        }
        else
        {
            Assert.IsType<NullReferenceException>(nativeError);
        }

        Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));
    }

    private static void AssertMembership<T>(T present, T absent, bool readOnly, string kind)
        where T : IEquatable<T>
    {
        T[]? values = kind switch
        {
            "null" or "constant-null" or "property-null" => null,
            "empty" or "property-empty" => [],
            "values" or "property-values" => [present],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var source = new ArraySource<T>(() => values);
        var property = kind.StartsWith("property-", StringComparison.Ordinal);
        var predicate = Predicate(values, source, readOnly, property, kind == "constant-null");
        var candidates = new[] { new Sample<T>(present), new Sample<T>(absent), new Sample<T>(default!) };
        var authored = predicate.Compile();
        var expected = candidates.Select(authored).ToArray();
        var priorReads = source.Reads;

        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var expectedReads = priorReads + (property ? 1 : 0);
        Assert.Equal(expectedReads, source.Reads);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        for (var index = 0; index < candidates.Length; index++)
        {
            Assert.Equal(expected[index], QueryFilterEvaluator.Evaluate(filter, candidates[index], reader));
            Assert.Equal(expected[index], compiled(candidates[index]));
        }

        Assert.Equal(expectedReads, source.Reads);
    }

    private static Expression<Func<Sample<T>, bool>> Predicate<T>(
        T[]? values, ArraySource<T> source, bool readOnly, bool property, bool constantNull)
        where T : IEquatable<T>
    {
        if (constantNull)
        {
            return readOnly
                ? e => ((ReadOnlySpan<T>)(T[]?)null).Contains(e.Value)
                : e => ((Span<T>)(T[]?)null).Contains(e.Value);
        }

        if (property)
        {
            return readOnly
                ? e => ((ReadOnlySpan<T>)source.Values).Contains(e.Value)
                : e => ((Span<T>)source.Values).Contains(e.Value);
        }

        return readOnly
            ? e => ((ReadOnlySpan<T>)values).Contains(e.Value)
            : e => ((Span<T>)values).Contains(e.Value);
    }

    private sealed record Sample<T>(T Value);

    private sealed class ArraySource<T>(Func<T[]?> factory)
    {
        public int Reads { get; private set; }

        public T[]? Values
        {
            get
            {
                Reads++;
                return factory();
            }
        }
    }
}
