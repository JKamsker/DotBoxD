using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryFilterValidationAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "AllocationMeasurement")]
    [InlineData(QueryFilterKind.MatchAll, true, 0)]
    [InlineData(QueryFilterKind.Not, false, 0)]
    [InlineData(QueryFilterKind.And, true, 32)]
    [InlineData(QueryFilterKind.Or, true, 32)]
    [InlineData(QueryFilterKind.Compare, true, 32)]
    [InlineData(QueryFilterKind.In, true, 64)]
    public void Evaluation_avoids_per_node_validation_allocations(QueryFilterKind kind, bool expected, int byteBudget)
    {
        var filter = CreateFilter(kind);
        var reader = new MemberValueReader();
        var value = new AttackTestEvent("alice", "bob", 1, 1);
        for (var i = 0; i < 1_000; i++)
        {
            Assert.Equal(expected, QueryFilterEvaluator.Evaluate(filter, value, reader));
        }

        const int iterations = 1_000;
        var matches = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            if (QueryFilterEvaluator.Evaluate(filter, value, reader))
            {
                matches++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{kind}: {allocated / iterations} bytes per evaluation.");
        Assert.Equal(expected ? iterations : 0, matches);
        // Existing path parsing and list traversal may allocate; validation must not add a scratch array per node.
        Assert.InRange(allocated, 0, (long)byteBudget * iterations);
    }

    private static QueryFilter CreateFilter(QueryFilterKind kind) => kind switch
    {
        QueryFilterKind.MatchAll => QueryFilter.MatchAll,
        QueryFilterKind.Not => QueryFilter.Not(QueryFilter.MatchAll),
        QueryFilterKind.And => QueryFilter.And([QueryFilter.MatchAll, QueryFilter.MatchAll]),
        QueryFilterKind.Or => QueryFilter.Or([QueryFilter.MatchAll, QueryFilter.MatchAll]),
        QueryFilterKind.Compare => QueryFilter.Compare(
            nameof(AttackTestEvent.AttackerId), QueryComparisonOperator.Equal, QueryValue.FromString("alice")),
        QueryFilterKind.In => QueryFilter.In(nameof(AttackTestEvent.AttackerId), [QueryValue.FromString("alice")]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
