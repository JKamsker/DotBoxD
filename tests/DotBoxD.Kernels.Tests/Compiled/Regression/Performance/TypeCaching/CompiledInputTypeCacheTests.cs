using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class CompiledInputTypeCacheTests
{
    [Fact]
    [Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
    public void Generated_input_shape_avoids_direct_builtin_structural_type_allocations()
    {
        const int iterations = 100_000;
        var i32 = SandboxValue.FromInt32(42);
        var list = SandboxValue.FromList([i32], SandboxType.I32);
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("key")] = i32
            },
            SandboxType.String,
            SandboxType.I32);
        var nestedList = SandboxValue.FromList([list], SandboxType.List(SandboxType.I32));
        var opaqueType = SandboxType.Scalar("MonsterId");
        var opaqueList = SandboxValue.FromList(
            [SandboxValue.FromOpaqueId("MonsterId", "monster-1")],
            opaqueType);

        _ = MeasureInput(list, LegacyListType, 1_000);
        _ = MeasureInput(list, CachedListType, 1_000);
        _ = MeasureInput(map, LegacyMapType, 1_000);
        _ = MeasureInput(map, CachedMapType, 1_000);
        _ = MeasureInput(nestedList, NestedListFallbackType, 1_000);
        _ = MeasureInput(opaqueList, OpaqueListFallbackType, 1_000);

        var legacyList = MeasureInput(list, LegacyListType, iterations);
        var cachedList = MeasureInput(list, CachedListType, iterations);
        var legacyMap = MeasureInput(map, LegacyMapType, iterations);
        var cachedMap = MeasureInput(map, CachedMapType, iterations);
        var nestedFallback = MeasureInput(nestedList, NestedListFallbackType, iterations);
        var opaqueFallback = MeasureInput(opaqueList, OpaqueListFallbackType, iterations);
        var listFactory = MeasureFactory(LegacyListType, SandboxType.I32, argumentIndex: 0, iterations);
        var mapFactory = MeasureFactory(LegacyMapType, SandboxType.I32, argumentIndex: 1, iterations);

        Assert.Equal(0, cachedList.Bytes);
        Assert.Equal(0, cachedMap.Bytes);
        Assert.Equal(112L * iterations, listFactory.Bytes);
        Assert.Equal(160L * iterations, mapFactory.Bytes);
        Assert.Equal(listFactory.Bytes, legacyList.Bytes - cachedList.Bytes);
        Assert.Equal(mapFactory.Bytes, legacyMap.Bytes - cachedMap.Bytes);
        Assert.Equal(224L * iterations, nestedFallback.Bytes);
        Assert.Equal(144L * iterations, opaqueFallback.Bytes);
        Assert.All(
            new[]
            {
                legacyList,
                cachedList,
                legacyMap,
                cachedMap,
                nestedFallback,
                opaqueFallback
            },
            measurement => Assert.Equal(iterations, measurement.Checksum));
    }

    private static Measurement MeasureInput(
        SandboxValue input,
        Func<SandboxType> expectedTypeFactory,
        int iterations)
    {
        ForceGc();
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            CompiledRuntime.ValidateEntrypointInput(input, parameterCount: 1);
            var value = CompiledRuntime.GetInputArgument(
                input,
                index: 0,
                parameterCount: 1,
                expectedType: expectedTypeFactory());
            checksum += ReferenceEquals(value, input) ? 1 : 0;
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static Measurement MeasureFactory(
        Func<SandboxType> factory,
        SandboxType expectedArgument,
        int argumentIndex,
        int iterations)
    {
        ForceGc();
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var type = factory();
            checksum += ReferenceEquals(type.Arguments[argumentIndex], expectedArgument) ? 1 : 0;
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(iterations, checksum);
        return new Measurement(allocatedBytes, checksum);
    }

    private static SandboxType LegacyListType()
        => CompiledRuntime.TypeList(CompiledRuntime.TypeScalar("I32"));

    private static SandboxType CachedListType()
        => CompiledRuntime.TypeListCached(CompiledRuntime.TypeScalar("I32"));

    private static SandboxType LegacyMapType()
        => CompiledRuntime.TypeMap(
            CompiledRuntime.TypeScalar("String"),
            CompiledRuntime.TypeScalar("I32"));

    private static SandboxType CachedMapType()
        => CompiledRuntime.TypeMapCached(
            CompiledRuntime.TypeScalar("String"),
            CompiledRuntime.TypeScalar("I32"));

    private static SandboxType NestedListFallbackType()
        => CompiledRuntime.TypeList(
            CompiledRuntime.TypeList(CompiledRuntime.TypeScalar("I32")));

    private static SandboxType OpaqueListFallbackType()
        => CompiledRuntime.TypeList(CompiledRuntime.TypeScalar("MonsterId"));

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(long Bytes, int Checksum);
}
