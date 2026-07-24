using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core.ValueShapeCaching;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ValueShapeCacheAllocationTests
{
    private const int ListIterations = 10_000;
    private const int MapIterations = 20_000;

    [Fact]
    public void Incremental_list_append_does_not_allocate_persistent_shape_boxes()
    {
        _ = MeasureListAppend(iterations: 500);

        var allocated = MeasureListAppend(ListIterations);

        Assert.True(
            allocated <= 720 * ListIterations,
            $"Incremental list append allocated {allocated / (double)ListIterations:F1} B/op.");
    }

    [Fact]
    public void Scalar_map_replace_does_not_allocate_persistent_shape_boxes()
    {
        _ = MeasureMapReplace(iterations: 500);

        var allocated = MeasureMapReplace(MapIterations);

        Assert.True(
            allocated <= 48 * MapIterations,
            $"Scalar map replacement allocated {allocated / (double)MapIterations:F1} B/op.");
    }

    private static long MeasureListAppend(int iterations)
    {
        var context = CreateContext(maxListLength: iterations, maxMapEntries: 0);
        var value = CompiledRuntime.ListEmpty(context, SandboxType.I32);
        var item = SandboxValue.FromInt32(1);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            value = CompiledRuntime.ListAdd(context, value, item);
        }

        GC.KeepAlive(value);
        return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private static long MeasureMapReplace(int iterations)
    {
        var context = CreateContext(maxListLength: 0, maxMapEntries: 1);
        var key = SandboxValue.FromInt32(1);
        var item = SandboxValue.FromInt32(2);
        var source = CompiledRuntime.MapEmpty(context, SandboxType.I32, SandboxType.I32);
        source = CompiledRuntime.MapSet(context, source, key, item);
        SandboxValue value = source;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            value = CompiledRuntime.MapSet(context, source, key, item);
        }

        GC.KeepAlive(value);
        return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private static SandboxContext CreateContext(int maxListLength, int maxMapEntries)
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxListLength: maxListLength,
            MaxMapEntries: maxMapEntries,
            MaxTotalCollectionElements: long.MaxValue);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}
