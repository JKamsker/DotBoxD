namespace DotBoxD.Kernels.Tests.Core.Contracts;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ExecutionPlanSealAllocationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Comparing_existing_seals_does_not_allocate(bool sameInstance, bool equalValue)
    {
        var first = new ExecutionPlanSeal(new string('a', 64));
        var second = sameInstance ? first : new ExecutionPlanSeal(new string(equalValue ? 'a' : 'b', 64));
        _ = Compare(first, second, 2_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var matches = Compare(first, second, 10_000);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(equalValue ? 10_000 : 0, matches);
            minimum = Math.Min(minimum, allocated);
        }

        Assert.Equal(0, minimum);
    }

    private static int Compare(ExecutionPlanSeal first, ExecutionPlanSeal second, int count)
    {
        var matches = 0;
        for (var index = 0; index < count; index++)
        {
            if (first.Equals(second))
            {
                matches++;
            }
        }
        return matches;
    }
}
