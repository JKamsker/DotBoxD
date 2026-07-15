using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class SubscriptionPipelineAllocationTests
{
    private const int WarmupIterations = 100;
    private const int MeasuredIterations = 10_000;

    [Fact]
    public void Empty_pipeline_publish_does_not_allocate_delivery_state()
    {
        using var server = PluginServer.Create();
        server.Subscriptions.On<Ping>();
        var value = new Ping(42);

        for (var i = 0; i < WarmupIterations; i++)
        {
            server.Subscriptions.Publish(value);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredIterations; i++)
        {
            server.Subscriptions.Publish(value);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0, allocated);
    }

    private readonly record struct Ping(int Value);
}
