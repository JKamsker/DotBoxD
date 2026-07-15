using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Resources.Performance;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class ResourceMeterHostCallTrackerTests
{
    [Fact]
    [Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
    public void Construction_without_limited_host_calls_allocates_only_the_meter()
    {
        const int iterations = 100_000;
        const int expectedBytesPerMeter = 128;
        var limits = new ResourceLimits();

        for (var i = 0; i < 20_000; i++)
        {
            GC.KeepAlive(new ResourceMeter(limits));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            GC.KeepAlive(new ResourceMeter(limits));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(expectedBytesPerMeter * (long)iterations, allocated);
    }

    [Fact]
    public void Reset_without_limited_host_calls_keeps_usage_clear()
    {
        var meter = new ResourceMeter(new ResourceLimits());

        meter.ResetForReuse();

        AssertOnlyHostCalls(meter.Snapshot(), expectedHostCalls: 0);
    }

    [Fact]
    public void Reset_clears_materialized_per_binding_counts()
    {
        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: 10));
        meter.ChargeHostCall("test.binding.a", maxCallsPerRun: 1);
        meter.ChargeHostCall("test.binding.b", maxCallsPerRun: 1);

        meter.ResetForReuse();

        AssertOnlyHostCalls(meter.Snapshot(), expectedHostCalls: 0);
        meter.ChargeHostCall("test.binding.a", maxCallsPerRun: 1);
        meter.ChargeHostCall("test.binding.b", maxCallsPerRun: 1);
        var exception = Assert.Throws<SandboxRuntimeException>(
            () => meter.ChargeHostCall("test.binding.a", maxCallsPerRun: 1));
        Assert.Equal(SandboxErrorCode.QuotaExceeded, exception.Error.Code);
        AssertOnlyHostCalls(meter.Snapshot(), expectedHostCalls: 3);
    }

    private static void AssertOnlyHostCalls(SandboxResourceUsage usage, int expectedHostCalls)
    {
        Assert.Equal(expectedHostCalls, usage.HostCalls);
        Assert.Equal(0, usage.FuelUsed);
        Assert.Equal(0, usage.LoopIterations);
        Assert.Equal(0, usage.AllocatedBytes);
        Assert.Equal(0, usage.FileBytesRead);
        Assert.Equal(0, usage.FileBytesWritten);
        Assert.Equal(0, usage.NetworkBytesRead);
        Assert.Equal(0, usage.NetworkBytesWritten);
        Assert.Equal(0, usage.LogEvents);
        Assert.Equal(0, usage.CollectionElements);
        Assert.Equal(0, usage.StringBytes);
    }
}
