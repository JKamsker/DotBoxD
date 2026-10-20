using System.Diagnostics;
using System.Linq.Expressions;
using DotBoxD.Queryable.Authoring;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Benchmarks.Queryable;

internal static class QuerySubscriptionChurnProbe
{
    public static void Run()
    {
        Console.WriteLine("Subscription churn: registration/removal of N handles, three samples (one at 10,000), median [min, max], setup included.");
        foreach (var shape in new[] { "shared", "many", "broad", "mixed" })
        {
            foreach (var count in new[] { 10, 100, 1000, 10_000 })
            {
                var predicates = Enumerable.Range(0, count).Select(i => Predicate(shape, i)).ToArray();
                var samples = count == 10_000 ? 1 : 3;
                var median = samples / 2;
                var register = new double[samples];
                var remove = new double[samples];
                var registerBytes = new long[samples];
                var removeBytes = new long[samples];
                for (var sample = 0; sample < samples; sample++)
                {
                    var host = new EventQueryHost();
                    var handles = new EventQuerySubscriptionHandle[count];
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    for (var i = 0; i < count; i++)
                    {
                        handles[i] = host.Query<Event>().Where(predicates[i])
                            .SubscribeAsync(static (_, _) => ValueTask.CompletedTask).GetAwaiter().GetResult();
                    }

                    register[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    registerBytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                    start = Stopwatch.GetTimestamp();
                    before = GC.GetAllocatedBytesForCurrentThread();
                    foreach (var handle in handles)
                    {
                        handle.Dispose();
                    }

                    remove[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    removeBytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (host.HasSubscriptions<Event>())
                    {
                        throw new InvalidOperationException("Removal left an active subscription.");
                    }
                }

                Array.Sort(register);
                Array.Sort(remove);
                Console.WriteLine($"{shape}/{count} register={register[median]:F2} ms [{register[0]:F2}, {register[^1]:F2}] {registerBytes[median]} B " +
                    $"remove={remove[median]:F2} ms [{remove[0]:F2}, {remove[^1]:F2}] {removeBytes[median]} B");
            }
        }
    }

    private static Expression<Func<Event, bool>> Predicate(string shape, int index)
    {
        if (shape == "broad" || (shape == "mixed" && index % 2 == 0))
        {
            return value => value.Damage > index;
        }

        var parameter = LinqExpression.Parameter(typeof(Event), "e");
        var mask = shape == "shared" ? 1 : index % 31 + 1;
        LinqExpression? body = null;
        for (var bit = 0; bit < 5; bit++)
        {
            if ((mask & (1 << bit)) != 0)
            {
                var comparison = LinqExpression.Equal(LinqExpression.Property(parameter, $"P{bit}"), LinqExpression.Constant(index));
                body = body is null ? comparison : LinqExpression.AndAlso(body, comparison);
            }
        }

        return LinqExpression.Lambda<Func<Event, bool>>(body!, parameter);
    }

    private sealed record Event(int P0, int P1, int P2, int P3, int P4, int Damage);
}
