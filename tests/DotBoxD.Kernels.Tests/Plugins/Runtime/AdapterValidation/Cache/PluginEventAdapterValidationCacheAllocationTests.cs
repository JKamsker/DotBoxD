using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class PluginEventAdapterValidationCacheAllocationTests
{
    private const int WarmupIterations = 20_000;
    private const int MeasurementIterations = 100_000;

    [Fact]
    public void Warm_gated_adapter_validation_does_not_allocate()
    {
        var environment = PluginEventAdapterValidationCacheTestFixture.Create(grantReadCapability: true);
        var cache = new PluginEventAdapterValidationCache();
        var adapter = new MutableValidationAdapter<GatedCacheEvent>();
        _ = Measure(environment, cache, adapter, WarmupIterations);

        var measurement = Measure(environment, cache, adapter, MeasurementIterations);

        Assert.Equal(MeasurementIterations, measurement.Checksum);
        Assert.Equal(0, measurement.AllocatedBytes);
    }

    private static Measurement Measure(
        PluginEventAdapterValidationCacheTestFixture.ValidationEnvironment environment,
        PluginEventAdapterValidationCache cache,
        MutableValidationAdapter<GatedCacheEvent> adapter,
        int iterations)
    {
        var checksum = 0L;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            checksum += environment.Validate(cache, adapter).Count;
        }

        GC.KeepAlive(adapter);
        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private readonly record struct Measurement(long AllocatedBytes, long Checksum);
}
