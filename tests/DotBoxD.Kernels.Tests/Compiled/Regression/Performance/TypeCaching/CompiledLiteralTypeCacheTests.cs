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

        _ = CompiledRuntime.ListLiteralValue(SandboxType.I32, []);
        _ = CompiledRuntime.ListLiteralValue(nestedType, []);
        _ = CompiledRuntime.MapLiteralValue(SandboxType.I32, SandboxType.I32, [], []);
        _ = CompiledRuntime.MapLiteralValue(SandboxType.I32, nestedType, [], []);
        _ = CompiledRuntime.TypeList(nestedType);
        _ = CompiledRuntime.TypeMap(SandboxType.I32, nestedType);

        var builtinList = MeasureList(SandboxType.I32, iterations);
        var nestedList = MeasureList(nestedType, iterations);
        var builtinMap = MeasureMap(SandboxType.I32, iterations);
        var nestedMap = MeasureMap(nestedType, iterations);
        var listTypeFactory = MeasureListTypeFactory(nestedType, iterations);
        var mapTypeFactory = MeasureMapTypeFactory(nestedType, iterations);

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
        ForceGc();
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
        ForceGc();
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
        ForceGc();
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
        ForceGc();
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var type = CompiledRuntime.TypeMap(SandboxType.I32, valueType);
            checksum += ReferenceEquals(type.Arguments[1], valueType) ? 1 : 0;
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(long Bytes, int Checksum);
}
