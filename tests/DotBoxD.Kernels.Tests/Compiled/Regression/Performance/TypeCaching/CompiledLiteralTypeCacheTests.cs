using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class CompiledLiteralTypeCacheTests
{
    [Fact]
    [Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
    public void Builtin_literal_validation_avoids_uncached_expected_type_allocation()
    {
        const int iterations = 100_000;
        var nestedType = SandboxType.List(SandboxType.I32);

        _ = MeasureList(SandboxType.I32, 1_000);
        _ = MeasureList(nestedType, 1_000);
        _ = MeasureMap(SandboxType.I32, 1_000);
        _ = MeasureMap(nestedType, 1_000);
        _ = MeasureListTypeFactory(nestedType, 1_000);
        _ = MeasureMapTypeFactory(nestedType, 1_000);

        var builtinList = MeasureSteadyState(() => MeasureList(SandboxType.I32, iterations));
        var nestedList = MeasureSteadyState(() => MeasureList(nestedType, iterations));
        var builtinMap = MeasureSteadyState(() => MeasureMap(SandboxType.I32, iterations));
        var nestedMap = MeasureSteadyState(() => MeasureMap(nestedType, iterations));
        var listTypeFactory = MeasureSteadyState(() => MeasureListTypeFactory(nestedType, iterations));
        var mapTypeFactory = MeasureSteadyState(() => MeasureMapTypeFactory(nestedType, iterations));

        Assert.Equal(iterations, builtinList.Checksum);
        Assert.Equal(iterations, nestedList.Checksum);
        Assert.Equal(iterations, builtinMap.Checksum);
        Assert.Equal(iterations, nestedMap.Checksum);
        Assert.Equal(iterations, listTypeFactory.Checksum);
        Assert.Equal(iterations, mapTypeFactory.Checksum);
        Assert.Equal(listTypeFactory.Bytes, nestedList.Bytes - builtinList.Bytes);
        Assert.Equal(mapTypeFactory.Bytes, nestedMap.Bytes - builtinMap.Bytes);
    }

    private static Measurement MeasureList(SandboxType itemType, int iterations)
    {
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var list = (ListValue)CompiledRuntime.ListLiteralValue(itemType, []);
            checksum += ReferenceEquals(list.ItemType, itemType) ? 1 : 0;
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static Measurement MeasureMap(SandboxType valueType, int iterations)
    {
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var map = (MapValue)CompiledRuntime.MapLiteralValue(SandboxType.I32, valueType, [], []);
            checksum += ReferenceEquals(map.ValueType, valueType) ? 1 : 0;
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static Measurement MeasureListTypeFactory(SandboxType itemType, int iterations)
    {
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var type = CompiledRuntime.TypeList(itemType);
            checksum += ReferenceEquals(type.Arguments[0], itemType) ? 1 : 0;
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static Measurement MeasureMapTypeFactory(SandboxType valueType, int iterations)
    {
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var type = CompiledRuntime.TypeMap(SandboxType.I32, valueType);
            checksum += ReferenceEquals(type.Arguments[1], valueType) ? 1 : 0;
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static Measurement MeasureSteadyState(Func<Measurement> measure)
    {
        // Unloading collectible types can rebuild the CLR cast cache on a measured thread.
        // Use the smallest of five warmed samples to exclude that one-time runtime allocation;
        // every sample still checks its work, and per-call allocation regressions affect them all.
        var best = measure();
        for (var i = 0; i < 4; i++)
        {
            var sample = measure();
            Assert.Equal(best.Checksum, sample.Checksum);
            if (sample.Bytes < best.Bytes)
            {
                best = sample;
            }
        }

        return best;
    }

    private readonly record struct Measurement(long Bytes, int Checksum);
}
