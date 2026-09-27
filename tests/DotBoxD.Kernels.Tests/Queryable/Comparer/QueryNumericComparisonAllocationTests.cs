using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryNumericComparisonAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "AllocationMeasurement")]
    [InlineData(QueryValueKind.Integer, false, QueryComparisonOperator.Equal)]
    [InlineData(QueryValueKind.Integer, true, QueryComparisonOperator.Equal)]
    [InlineData(QueryValueKind.Integer, false, QueryComparisonOperator.GreaterThan)]
    [InlineData(QueryValueKind.Integer, true, QueryComparisonOperator.GreaterThan)]
    [InlineData(QueryValueKind.UnsignedInteger, false, QueryComparisonOperator.Equal)]
    [InlineData(QueryValueKind.UnsignedInteger, true, QueryComparisonOperator.Equal)]
    [InlineData(QueryValueKind.UnsignedInteger, false, QueryComparisonOperator.GreaterThan)]
    [InlineData(QueryValueKind.UnsignedInteger, true, QueryComparisonOperator.GreaterThan)]
    [InlineData(QueryValueKind.Decimal, false, QueryComparisonOperator.Equal)]
    [InlineData(QueryValueKind.Decimal, true, QueryComparisonOperator.Equal)]
    [InlineData(QueryValueKind.Decimal, false, QueryComparisonOperator.GreaterThan)]
    [InlineData(QueryValueKind.Decimal, true, QueryComparisonOperator.GreaterThan)]
    // Keep tiered optimization of this measurement loop outside the allocation budget.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public void CompareNumeric_DoesNotAllocatePerComparison(
        QueryValueKind kind,
        bool floatingActual,
        QueryComparisonOperator comparison)
    {
        var expected = kind switch
        {
            QueryValueKind.Integer => QueryValue.FromInteger(42),
            QueryValueKind.UnsignedInteger => QueryValue.FromUnsignedInteger(42),
            QueryValueKind.Decimal => QueryValue.FromDecimal(42),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var actualValue = comparison == QueryComparisonOperator.Equal ? 42 : 43;
        object actual = floatingActual ? (object)(double)actualValue : (long)actualValue;
        for (var i = 0; i < 100; i++)
        {
            Assert.True(QueryValueComparer.Compare(actual, comparison, expected, ignoreCase: false));
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
        output.WriteLine($"{kind} {comparison}, floating actual={floatingActual}: {allocated / iterations} bytes per comparison.");
        Assert.Equal(iterations, matches);
        Assert.Equal(0, allocated);
    }
}
