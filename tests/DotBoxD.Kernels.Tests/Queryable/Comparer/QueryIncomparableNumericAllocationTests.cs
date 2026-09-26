using System.Runtime.CompilerServices;
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
    [InlineData("timestamp", false, QueryComparisonOperator.Equal)]
    [InlineData("timestamp", false, QueryComparisonOperator.NotEqual)]
    [InlineData("timestamp", false, QueryComparisonOperator.GreaterThan)]
    [InlineData("timestamp", true, QueryComparisonOperator.Equal)]
    [InlineData("timestamp", true, QueryComparisonOperator.NotEqual)]
    [InlineData("timestamp", true, QueryComparisonOperator.GreaterThan)]
    [InlineData("char", false, QueryComparisonOperator.Equal)]
    [InlineData("char", false, QueryComparisonOperator.NotEqual)]
    [InlineData("char", false, QueryComparisonOperator.GreaterThan)]
    [InlineData("char", true, QueryComparisonOperator.Equal)]
    [InlineData("char", true, QueryComparisonOperator.NotEqual)]
    [InlineData("char", true, QueryComparisonOperator.GreaterThan)]
    [InlineData("dbnull", false, QueryComparisonOperator.Equal)]
    [InlineData("dbnull", false, QueryComparisonOperator.NotEqual)]
    [InlineData("dbnull", false, QueryComparisonOperator.GreaterThan)]
    [InlineData("dbnull", true, QueryComparisonOperator.Equal)]
    [InlineData("dbnull", true, QueryComparisonOperator.NotEqual)]
    [InlineData("dbnull", true, QueryComparisonOperator.GreaterThan)]
    // Keep tiered optimization of this measurement loop outside the allocation budget.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public void Incomparable_values_are_rejected_without_allocations(
        string actualKind, bool exactExpected, QueryComparisonOperator comparison)
    {
        object actual = actualKind switch
        {
            "guid" => Guid.Empty,
            "date" => new DateOnly(2026, 1, 1),
            "object" => new object(),
            "timestamp" => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "char" => 'a',
            "dbnull" => DBNull.Value,
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
