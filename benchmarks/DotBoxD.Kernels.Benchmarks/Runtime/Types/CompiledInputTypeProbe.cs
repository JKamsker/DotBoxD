using System.Diagnostics;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime.Types;

internal static class CompiledInputTypeProbe
{
    private const int WarmupIterations = 100_000;
    private const int Iterations = 2_000_000;

    public static void Run()
    {
        var i32 = SandboxValue.FromInt32(42);
        var list = SandboxValue.FromList([i32], SandboxType.I32);
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("key")] = i32
            },
            SandboxType.String,
            SandboxType.I32);
        var nestedItemType = SandboxType.List(SandboxType.I32);
        var nestedList = SandboxValue.FromList([list], nestedItemType);
        var opaqueType = SandboxType.Scalar("MonsterId");
        var opaqueList = SandboxValue.FromList(
            [SandboxValue.FromOpaqueId("MonsterId", "monster-1")],
            opaqueType);

        _ = Measure(WarmupIterations, list, LegacyListType);
        _ = Measure(WarmupIterations, list, CachedListType);
        _ = Measure(WarmupIterations, map, LegacyMapType);
        _ = Measure(WarmupIterations, map, CachedMapType);
        _ = Measure(WarmupIterations, nestedList, NestedListFallbackType);
        _ = Measure(WarmupIterations, nestedList, NestedListCachedChildType);
        _ = Measure(WarmupIterations, opaqueList, OpaqueListFallbackType);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine("case                              ms      allocated B      B/op checksum");
        Write("List<I32> legacy input", Measure(Iterations, list, LegacyListType));
        Write("List<I32> cached input", Measure(Iterations, list, CachedListType));
        Write("Map<String,I32> legacy input", Measure(Iterations, map, LegacyMapType));
        Write("Map<String,I32> cached input", Measure(Iterations, map, CachedMapType));
        Write("List<List<I32>> fallback", Measure(Iterations, nestedList, NestedListFallbackType));
        Write("List<List<I32>> cached child", Measure(Iterations, nestedList, NestedListCachedChildType));
        Write("List<MonsterId> fallback", Measure(Iterations, opaqueList, OpaqueListFallbackType));
    }

    private static Measurement Measure(
        int iterations,
        SandboxValue input,
        Func<SandboxType> expectedTypeFactory)
    {
        ForceGc();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            CompiledRuntime.ValidateEntrypointInput(input, parameterCount: 1);
            var value = CompiledRuntime.GetInputArgument(
                input,
                index: 0,
                parameterCount: 1,
                expectedType: expectedTypeFactory());
            if (!ReferenceEquals(value, input))
            {
                throw new InvalidOperationException("compiled input validation changed the value instance");
            }

            checksum++;
        }

        watch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement(watch.Elapsed.TotalMilliseconds, allocatedBytes, checksum);
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

    private static SandboxType NestedListCachedChildType()
        => CompiledRuntime.TypeList(
            CompiledRuntime.TypeListCached(CompiledRuntime.TypeScalar("I32")));

    private static SandboxType OpaqueListFallbackType()
        => CompiledRuntime.TypeList(CompiledRuntime.TypeScalar("MonsterId"));

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-33} {measurement.Milliseconds,8:N1} {measurement.AllocatedBytes,16:N0} " +
            $"{measurement.AllocatedBytes / (double)Iterations,9:N1} {measurement.Checksum,8:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        int Checksum);
}
