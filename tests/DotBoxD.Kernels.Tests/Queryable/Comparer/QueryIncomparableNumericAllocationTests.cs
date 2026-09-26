using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryIncomparableNumericAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "AllocationMeasurement")]
    [InlineData("guid", false, QueryComparisonOperator.Equal)]
    [InlineData("guid", false, QueryComparisonOperator.GreaterThan)]
    [InlineData("guid", true, QueryComparisonOperator.Equal)]
    [InlineData("guid", true, QueryComparisonOperator.GreaterThan)]
    [InlineData("date", false, QueryComparisonOperator.Equal)]
    [InlineData("date", false, QueryComparisonOperator.GreaterThan)]
    [InlineData("date", true, QueryComparisonOperator.Equal)]
    [InlineData("date", true, QueryComparisonOperator.GreaterThan)]
    [InlineData("object", false, QueryComparisonOperator.Equal)]
    [InlineData("object", false, QueryComparisonOperator.GreaterThan)]
    [InlineData("object", true, QueryComparisonOperator.Equal)]
    [InlineData("object", true, QueryComparisonOperator.GreaterThan)]
    public void Nonconvertible_values_are_incomparable_without_allocations(
        string actualKind, bool exactExpected, QueryComparisonOperator comparison)
    {
        object actual = actualKind switch
        {
            "guid" => Guid.Empty,
            "date" => new DateOnly(2026, 1, 1),
            "object" => new object(),
            _ => throw new ArgumentOutOfRangeException(nameof(actualKind))
        };
        var expected = exactExpected ? QueryValue.FromDecimal(1.5m) : QueryValue.FromNumber(1.5);
        for (var i = 0; i < 100; i++)
        {
            Assert.False(QueryValueComparer.Compare(actual, comparison, expected, ignoreCase: false));
        }

        const int iterations = 1_000;
        var matches = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            if (QueryValueComparer.Compare(actual, comparison, expected, ignoreCase: false))
            {
                matches++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{actualKind} {comparison} {expected.Kind}: {allocated / iterations} bytes per comparison.");
        Assert.Equal(0, matches);
        Assert.Equal(0, allocated);
    }
}
