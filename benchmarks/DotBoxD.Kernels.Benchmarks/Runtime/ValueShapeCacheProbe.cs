using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Kernels.Runtime;

internal static class ValueShapeCacheProbe
{
    private const int Warmup = 500;
    private const int ListIterations = 10_000;
    private const int MapIterations = 20_000;
    private const int ControlIterations = 1_000_000;

    public static void Run()
    {
        _ = MeasureListAppend(Warmup);
        _ = MeasureMapReplace(Warmup);
        _ = MeasureListConstruction(Warmup);
        _ = MeasureOwnedListReset(Warmup);

        Write("CompiledRuntime.ListAdd scalar shape cache", MeasureListAppend(ListIterations));
        Write("CompiledRuntime.MapSet scalar replace cache", MeasureMapReplace(MapIterations));
        Write("ListValue empty construction control", MeasureListConstruction(ControlIterations));
        Write("ListValue owned reset control", MeasureOwnedListReset(ControlIterations));
    }

    private static Measurement MeasureListAppend(int iterations)
    {
        var context = CreateContext(maxListLength: iterations, maxMapEntries: 0);
        var value = CompiledRuntime.ListEmpty(context, SandboxType.I32);
        var item = SandboxValue.FromInt32(1);

        Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            value = CompiledRuntime.ListAdd(context, value, item);
        }

        sw.Stop();
        GC.KeepAlive(value);
        return Capture(iterations, context, sw, allocatedBefore);
    }

    private static Measurement MeasureMapReplace(int iterations)
    {
        var context = CreateContext(maxListLength: 0, maxMapEntries: 1);
        var key = SandboxValue.FromInt32(1);
        var item = SandboxValue.FromInt32(2);
        var source = CompiledRuntime.MapEmpty(context, SandboxType.I32, SandboxType.I32);
        source = CompiledRuntime.MapSet(context, source, key, item);
        SandboxValue value = source;

        Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            value = CompiledRuntime.MapSet(context, source, key, item);
        }

        sw.Stop();
        GC.KeepAlive(value);
        return Capture(iterations, context, sw, allocatedBefore);
    }

    private static Measurement MeasureListConstruction(int iterations)
    {
        var values = Array.Empty<SandboxValue>();
        ListValue? value = null;
        Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            value = new ListValue(values, SandboxType.I32);
        }

        sw.Stop();
        GC.KeepAlive(value);
        return Capture(iterations, sw, allocatedBefore);
    }

    private static Measurement MeasureOwnedListReset(int iterations)
    {
        var first = new[] { SandboxValue.FromInt32(1) };
        var second = new[] { SandboxValue.FromInt32(2) };
        var value = ListValue.FromOwnedValues(first, SandboxType.I32);
        Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        long checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            value.ResetOwnedValues((i & 1) == 0 ? first : second);
            checksum += ((I32Value)value[0]).Value;
        }

        sw.Stop();
        if (checksum != (long)iterations / 2 * 3)
        {
            throw new InvalidOperationException($"Owned-list reset checksum changed: {checksum}.");
        }

        GC.KeepAlive(value);
        return Capture(iterations, sw, allocatedBefore);
    }

    private static Measurement Capture(
        int iterations,
        SandboxContext context,
        Stopwatch sw,
        long allocatedBefore)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement(
            iterations,
            sw.Elapsed.TotalMilliseconds,
            allocated,
            context.Budget.FuelUsed,
            context.Budget.CollectionElements);
    }

    private static Measurement Capture(
        int iterations,
        Stopwatch sw,
        long allocatedBefore) =>
        new(
            iterations,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            FuelUsed: 0,
            CollectionElements: 0);

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(string name, Measurement measurement)
    {
        var nanosecondsPerOperation =
            measurement.Milliseconds * 1_000_000 / measurement.Iterations;
        var bytesPerOperation =
            measurement.AllocatedBytes / (double)measurement.Iterations;
        Console.WriteLine(
            $"{name,-48} {measurement.Milliseconds,8:N1} ms " +
            $"{nanosecondsPerOperation,10:N1} ns/op {measurement.AllocatedBytes,14:N0} B " +
            $"{bytesPerOperation,8:N1} B/op");
        if (measurement.FuelUsed != 0 || measurement.CollectionElements != 0)
        {
            Console.WriteLine(
                $"usage: fuel={measurement.FuelUsed:N0}, " +
                $"collectionElements={measurement.CollectionElements:N0}");
        }
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

    private readonly record struct Measurement(
        int Iterations,
        double Milliseconds,
        long AllocatedBytes,
        long FuelUsed,
        long CollectionElements);
}
