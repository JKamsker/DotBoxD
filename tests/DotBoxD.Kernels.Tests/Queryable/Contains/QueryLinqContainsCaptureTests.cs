using System.Collections;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryLinqContainsCaptureTests
{
    [Theory]
    [MemberData(nameof(QueryLinqContainsTests.DefaultCases), MemberType = typeof(QueryLinqContainsTests))]
    public void Explicit_snapshots_keep_enumeration_membership(string kind)
    {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alice" };
        var values = LinqContainsCases.Forward(kind, source).ToArray();

        QueryLinqContainsTests.AssertParity(e => values.Contains(e.AttackerId));
    }

    [Fact]
    public void SelectMany_captures_each_selected_collection_once()
    {
        var calls = 0;
        IEnumerable<string> Select(int number)
        {
            calls++;
            return calls <= 2 ? new[] { number == 0 ? "alice" : "other" } : new LinqContainsCases.CustomList();
        }

        var values = new[] { 0, 1 }.SelectMany(Select);
        var capture = new Capture(values);
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => capture.Values.Contains(e.AttackerId));

        Assert.Equal(1, capture.Reads);
        Assert.Equal(2, calls);
        Assert.Equal(2, filter.Values.Count);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var name in new[] { "alice", "other", "ALICE" })
        {
            var value = new AttackTestEvent(name, "target", 1, 1);
            Assert.Equal(name != "ALICE", QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(name != "ALICE", compiled(value));
        }

        Assert.Equal(2, calls);
    }

    [Fact]
    public void SelectMany_rejects_the_actual_selected_instance()
    {
        var calls = 0;
        IEnumerable<string> Select(int _)
        {
            calls++;
            return calls == 1 ? new LinqContainsCases.CustomList() : new[] { "alice" };
        }

        var values = new[] { 1 }.SelectMany(Select);
        Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => values.Contains(e.AttackerId)));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selector_failures_preserve_cancellation_and_wrap_other_errors(bool canceled)
    {
        Exception failure = canceled ? new OperationCanceledException() : new InvalidOperationException("selector failed");
        IEnumerable<string> Select(int _) => throw failure;
        var values = new[] { 1 }.SelectMany(Select);

        var error = Record.Exception(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => values.Contains(e.AttackerId)));

        Assert.Same(failure, canceled ? error : Assert.IsType<QueryTranslationException>(error).InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Set_operators_keep_duplicate_values_compact(bool union)
    {
        var source = Enumerable.Repeat("alice", 2_000);
        var values = union ? source.Union(new[] { "alice" }) : source.Distinct();

        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => values.Contains(e.AttackerId));

        Assert.Single(filter.Values);
    }

    [Fact]
    public void Covariant_iterators_use_the_requested_element_type()
    {
        IEnumerable<object> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alice" }.Distinct();

        QueryLinqContainsTests.AssertParity(e => values.Contains(e.AttackerId));
    }

    [Fact]
    public void Cyclic_selected_sources_produce_a_translation_diagnostic()
    {
        IEnumerable<string> values = [];
        values = new[] { 1 }.SelectMany(_ => values);

        var error = Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => values.Contains(e.AttackerId)));

        Assert.Contains("cyclic", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_selected_sources_are_captured_without_a_false_cycle()
    {
        var shared = new[] { "alice" }.Reverse();
        var values = new[] { 1, 2 }.SelectMany(_ => shared);

        QueryLinqContainsTests.AssertParity(e => values.Contains(e.AttackerId));
    }

    [Fact]
    public void Generic_source_enumeration_is_used_for_selected_sources()
    {
        var values = new SplitEnumeration().SelectMany(x => new[] { x });

        QueryLinqContainsTests.AssertParity(e => values.Contains(e.AttackerId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordering_selectors_are_not_evaluated_by_membership_capture(bool descending)
    {
        static int Key(string _) => throw new InvalidOperationException("Contains does not order the source.");
        var source = new[] { "alice", "other" };
        var values = descending ? source.OrderByDescending(Key) : source.OrderBy(Key);

        QueryLinqContainsTests.AssertParity(e => values.Contains(e.AttackerId));
    }

    private sealed class SplitEnumeration : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)new[] { "alice" }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => throw new InvalidOperationException("Use the generic enumerator.");
    }

    private sealed class Capture(IEnumerable<string> values)
    {
        public int Reads { get; private set; }

        public IEnumerable<string> Values
        {
            get
            {
                Reads++;
                return values;
            }
        }
    }
}
