namespace DotBoxD.Kernels.Tests.Execution;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class PreparedPlanCacheAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_plan_identity_lookup_does_not_allocate(bool byReference)
    {
        var fixture = await PreparedPlanIdentityFixture.CreateAsync();
        var plan = fixture.Plan;
        var cache = new PreparedPlanIntegrityCache();
        cache.Register(plan);
        _ = Lookup(cache, plan, byReference, 2_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var found = Lookup(cache, plan, byReference, 10_000);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(10_000, found);
            minimum = Math.Min(minimum, allocated);
        }

        Assert.Equal(0, minimum);
        GC.KeepAlive(plan);
    }

    private static int Lookup(PreparedPlanIntegrityCache cache, ExecutionPlan plan, bool byReference, int count)
    {
        var found = 0;
        for (var i = 0; i < count; i++)
        {
            if (byReference
                    ? cache.ContainsTrustedReference(plan)
                    : cache.TryGetTrusted(plan.PlanSeal, out var trusted) && ReferenceEquals(plan, trusted))
            {
                found++;
            }
        }
        return found;
    }
}
