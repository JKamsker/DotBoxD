using DotBoxD.Hosting;

namespace DotBoxD.Kernels.Tests.Workers;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class WorkerPlanCacheAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reused_prepared_worker_plan_lookup_does_not_allocate(bool equalSeal)
    {
        using var fixture = new WorkerPlanCacheFixture();
        using var cache = new WorkerPreparedPlanCache();
        var original = fixture.Prepare("lookup");
        var request = equalSeal ? fixture.Reprepare(original) : original;
        cache.TryAdd(original, original);
        _ = Lookup(cache, request, original, 2_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var found = Lookup(cache, request, original, 10_000);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(10_000, found);
            minimum = Math.Min(minimum, allocated);
        }

        Assert.Equal(0, minimum);
    }

    private static int Lookup(WorkerPreparedPlanCache cache, ExecutionPlan request, ExecutionPlan expected, int count)
    {
        var found = 0;
        for (var i = 0; i < count; i++)
        {
            if (cache.TryGet(request, out var prepared) && ReferenceEquals(expected, prepared))
            {
                found++;
            }
        }
        return found;
    }
}
