using DotBoxD.Plugins;
using DotBoxD.Plugins.Indexing;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Plugins.Indexing;

public sealed class EventIndexNumericAllocationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "AllocationMeasurement")]
    public void PromotingNumericOperands_DoesNotAddPerCheckAllocations()
    {
        var integerBytes = Measure(5);
        var doubleBytes = Measure(5.4d);
        var decimalBytes = Measure(5.4m);
        output.WriteLine($"Bytes per check: integer {integerBytes / 1_000}, double {doubleBytes / 1_000}, decimal {decimalBytes / 1_000}.");

        Assert.True(doubleBytes <= integerBytes + 1_000, $"Double promotion allocated {doubleBytes} bytes versus {integerBytes}.");
        Assert.True(decimalBytes <= integerBytes + 1_000, $"Decimal promotion allocated {decimalBytes} bytes versus {integerBytes}.");
    }

    private static long Measure(object bound)
    {
        var matcher = EventIndexMatcher<Sample>.Create(
            [new IndexedPredicate("Value", IndexPredicateOperator.LessThan, bound, bound.GetType().Name)]);
        var value = new Sample(4);
        for (var i = 0; i < 100; i++)
        {
            matcher.CouldMatch(value);
        }

        var matches = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            if (matcher.CouldMatch(value))
            {
                matches++;
            }
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1_000, matches);
        return bytes;
    }

    private sealed record Sample([property: EventIndexKey] int Value);
}
