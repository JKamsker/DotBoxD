using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryFilterValidationAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "AllocationMeasurement")]
    [InlineData(QueryFilterKind.MatchAll, true)]
    [InlineData(QueryFilterKind.Not, false)]
    [InlineData(QueryFilterKind.And, true)]
    [InlineData(QueryFilterKind.Or, true)]
    [InlineData(QueryFilterKind.Compare, true)]
    [InlineData(QueryFilterKind.In, true)]
    public void Evaluation_of_reference_value_filters_does_not_allocate(QueryFilterKind kind, bool expected)
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
        Assert.Equal(0, allocated);
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
