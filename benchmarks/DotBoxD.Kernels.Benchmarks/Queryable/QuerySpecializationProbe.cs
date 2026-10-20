using DotBoxD.Kernels.Benchmarks.Measurements;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Benchmarks.Queryable;

internal static class QuerySpecializationProbe
{
    private static int _matches;

    public static void Run()
    {
        Console.WriteLine("Query specialization: median [min, max] of five samples; allocations exclude setup/promotion.");
        foreach (var nested in new[] { false, true })
        {
            var path = nested ? "Nested.Damage" : "Damage";
            var filter = QueryFilter.Compare(path, QueryComparisonOperator.GreaterThan, QueryValue.FromInteger(10));
            var reader = new MemberValueReader(typeof(Event));
            var compiled = QueryFilterCompiler.Compile(filter, reader);
            foreach (var hit in new[] { false, true })
            {
                var e = new Event(hit ? 11 : 9);
                ScalingProbeMeter.Measure($"{path}/{hit}/interpreted", () => Count(QueryFilterEvaluator.Evaluate(filter, e, reader)));
                ScalingProbeMeter.Measure($"{path}/{hit}/compiled", () => Count(compiled(e)));
                foreach (var subscribers in new[] { 1, 100, 1000 })
                {
                    var host = new EventQueryHost();
                    var handles = new List<EventQuerySubscriptionHandle>();
                    var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
                    for (var i = 0; i < subscribers; i++)
                    {
                        var query = host.Query<Event>();
                        query = nested ? query.Where(value => value.Nested.Damage > 10) : query.Where(value => value.Damage > 10);
                        handles.Add(query.SubscribeAsync(static (_, _) => ValueTask.CompletedTask).GetAwaiter().GetResult());
                    }

                    ScalingProbeMeter.Measure($"{path}/{hit}/publish/{subscribers}",
                        () => host.PublishAsync(e, context).GetAwaiter().GetResult(), Math.Max(1000, 100_000 / subscribers));
                    foreach (var handle in handles)
                    {
                        handle.Dispose();
                    }
                }
            }
        }

        var dynamicReader = new MemberValueReader();
        var control = QueryFilterCompiler.Compile(
            QueryFilter.Compare("Damage", QueryComparisonOperator.GreaterThan, QueryValue.FromInteger(10)), dynamicReader);
        var controlEvent = new Event(11);
        ScalingProbeMeter.Measure("runtime-root/compiled/control", () => Count(control(controlEvent)));
        Console.WriteLine($"predicate checksum: {_matches}");
    }

    public static void RunMembership(bool prepare = true)
    {
        Console.WriteLine("IN lookup: median [min, max] of five samples; allocations exclude setup/promotion.");
        var reader = new MemberValueReader(typeof(MembershipEvent));
        foreach (var strings in new[] { false, true })
        {
            foreach (var count in new[] { 1, 4, 8, 32, 64, 256 })
            {
                var values = Enumerable.Range(0, count)
                    .Select(i => strings ? QueryValue.FromString($"value-{i}") : QueryValue.FromInteger(i)).ToArray();
                var filter = QueryFilter.In("Value", values);
                if (!prepare)
                {
                    filter = filter with { Values = values };
                }
                var compiled = QueryFilterCompiler.Compile(filter, reader);
                foreach (var position in new[] { -1, 0, count / 2, count - 1 }.Distinct())
                {
                    var e = new MembershipEvent(strings ? $"value-{position}" : position);
                    ScalingProbeMeter.Measure($"{(strings ? "string" : "integer")}/{count}/{position}/compiled", () => Count(compiled(e)));
                }
            }
        }

        Console.WriteLine($"predicate checksum: {_matches}");
    }

    private static void Count(bool match) => _matches += match ? 1 : 0;

    private sealed class Event(int damage)
    {
        public int Damage { get; } = damage;
        public NestedEvent Nested { get; } = new(damage);
    }

    private sealed record NestedEvent(int Damage);
    private sealed record MembershipEvent(object Value);
}
