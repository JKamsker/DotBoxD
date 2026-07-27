namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ResultHookFanoutAllocationTests
{
    private const int WarmupIterations = 100;
    private const int MeasuredIterations = 10_000;

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(8, true)]
    [InlineData(8, false)]
    public void Warm_result_hook_fanout_does_not_allocate(int pipelineCount, bool includeHandlers)
    {
        using var scenario = ResultHookFanoutScenario.Create(pipelineCount, includeHandlers);
        for (var i = 0; i < WarmupIterations; i++)
        {
            scenario.Dispatch();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialDispatchCount = scenario.DispatchCount;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredIterations; i++)
        {
            scenario.Dispatch();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(MeasuredIterations, scenario.DispatchCount - initialDispatchCount);
        Assert.Equal(0, allocated);
    }
}
