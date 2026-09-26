using DotBoxD.Plugins;
using DotBoxD.Plugins.Indexing;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Plugins.Indexing;

public sealed class EventIndexMatcherAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "AllocationMeasurement")]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(1, false)]
    [InlineData(16, false)]
    public void StringChecks_DoNotAllocateAnEnumerator(int checkCount, bool expected)
    {
        var predicate = new IndexedPredicate("Name", IndexPredicateOperator.Equals, "selected", "string");
        var predicates = Enumerable.Repeat(predicate, checkCount).ToArray();
        if (!expected)
        {
            predicates[^1] = predicate with { Value = "other" };
        }

        var matcher = EventIndexMatcher<Sample>.Create(predicates);
        var value = new Sample("selected");
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
        output.WriteLine($"{checkCount} string checks, result {expected}: {bytes / 1_000} bytes per matcher call.");
        Assert.Equal(expected ? 1_000 : 0, matches);
        Assert.True(bytes <= 1_000, $"Matcher allocated {bytes} bytes over 1,000 calls.");
    }

    private sealed record Sample([property: EventIndexKey] string Name);
}
