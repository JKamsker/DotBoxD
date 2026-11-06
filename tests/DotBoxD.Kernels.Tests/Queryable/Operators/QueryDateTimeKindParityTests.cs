using System.Linq.Expressions;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

// C# DateTime ==, <, and Contains (DateTime.Equals) compare raw Ticks and ignore Kind, so a captured Local/Unspecified
// literal and a Utc member with the same clock reading are equal in LINQ-to-objects. The query tiers must
// agree with that on every machine instead of converting Local values through the host's time zone.
// (On a UTC host the Local conversion is the identity, so run with e.g. TZ=America/New_York to exercise it.)
public sealed class QueryDateTimeKindParityTests
{
    private static readonly DateTime[] Clock =
    [
        new(2026, 3, 1, 10, 0, 0),
        new(2026, 3, 1, 13, 0, 0),
        new(2026, 3, 1, 15, 0, 0),
    ];

    private static readonly DateTimeKind[] Kinds = [DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified];

    private static IEnumerable<DateTime> Values()
        => Clock.SelectMany(clock => Kinds.Select(kind => DateTime.SpecifyKind(clock, kind)));

    [Fact]
    public void Scalar_comparisons_ignore_kind_like_linq_in_both_tiers()
    {
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var member = LinqExpression.Property(parameter, nameof(Sample.When));
        foreach (var op in QueryComparisonOperatorMethodTests.ComparisonOperators)
        {
            foreach (var captured in Values())
            {
                var body = LinqExpression.MakeBinary(op, member, LinqExpression.Constant(captured));
                AssertParity(LinqExpression.Lambda<Func<Sample, bool>>(body, parameter));
            }
        }
    }

    [Fact]
    public void Contains_ignores_kind_like_linq_in_both_tiers()
    {
        foreach (var captured in Values())
        {
            var set = new[] { captured, captured.AddHours(1) };
            AssertParity(e => set.Contains(e.When));
            var large = Enumerable.Range(0, 9).Select(hours => captured.AddHours(hours)).ToArray();
            AssertParity(e => large.Contains(e.When));
        }
    }

    [Fact]
    public async Task Routed_equality_ignores_kind_like_linq()
    {
        var captured = DateTime.SpecifyKind(Clock[1], DateTimeKind.Local);
        Expression<Func<Sample, bool>> predicate = e => e.When == captured;
        var host = new EventQueryHost();
        var actual = new List<Sample>();
        using var handle = await host.Query<Sample>().Where(predicate).SubscribeAsync((value, _) =>
        {
            actual.Add(value);
            return ValueTask.CompletedTask;
        });
        var events = Values().Select(value => new Sample(value)).ToArray();
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);

        foreach (var value in events)
        {
            await host.PublishAsync(value, context);
        }

        Assert.Equal(
            events.Where(predicate.Compile()).Select(e => (e.When.Ticks, e.When.Kind)),
            actual.Select(e => (e.When.Ticks, e.When.Kind)));
    }

    private static void AssertParity(Expression<Func<Sample, bool>> predicate)
    {
        var authored = predicate.Compile();
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var value in Values())
        {
            var input = new Sample(value);
            var expected = authored(input);
            Assert.True(expected == QueryFilterEvaluator.Evaluate(filter, input, reader), $"interpreted {predicate} for {value:O} ({value.Kind})");
            Assert.True(expected == compiled(input), $"compiled {predicate} for {value:O} ({value.Kind})");
        }
    }

    private sealed record Sample(DateTime When);
}
