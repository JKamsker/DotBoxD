using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using static DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff.ValueShapeHandoffProbeSupport;
using static DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff.ValueShapeProducerConsumerRunner;

namespace DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff;

internal static class ValueShapeCrossThreadHandoffProbe
{
    private const int WarmupIterations = 500;
    private const int ListIterations = 10_000;
    private const int MapIterations = 20_000;

    public static void Run()
    {
        var coldMisses = ValueShapeColdMissProbe.Measure();
        var sameThreadList = MeasureSameThreadList();
        var crossThreadList = MeasureCrossThreadList();
        var sameThreadMap = MeasureSameThreadMap();
        var crossThreadMap = MeasureCrossThreadMap();

        Console.WriteLine();
        Console.WriteLine("ValueShapeCache producer/consumer handoff probe");
        Write("List<I32> x256 same-thread charge", sameThreadList);
        Write("List<I32> x256 cross-thread charge", crossThreadList);
        Write("Map<I32,I32> x256 same-thread charge", sameThreadMap);
        Write("Map<I32,I32> x256 cross-thread charge", crossThreadMap);
        Write("List<I32> x1 cold miss, 16 publishers", coldMisses.List);
        Write("Map<I32,I32> x1 cold miss, 16 publishers", coldMisses.Map);
        Write("Record<I32> x1 cold miss, 16 publishers", coldMisses.Record);
        Console.WriteLine(
            $"invariants: list nodes={ListNodes}, map nodes={MapNodes}, " +
            $"list fuel/op={ListNodes / 64}, map fuel/op={MapNodes / 64}");
    }

    private static Measurement MeasureSameThreadList() => RunOnDedicatedThread(() =>
    {
        _ = MeasureSameThreadListCore(WarmupIterations);
        return MeasureSameThreadListCore(ListIterations);
    });

    private static Measurement MeasureCrossThreadList()
        => RunProducerConsumer<ListValue, Measurement>(ProduceLists, ConsumeLists);

    private static Measurement MeasureSameThreadMap() => RunOnDedicatedThread(() =>
    {
        _ = MeasureSameThreadMapCore(WarmupIterations);
        return MeasureSameThreadMapCore(MapIterations);
    });

    private static Measurement MeasureCrossThreadMap()
        => RunProducerConsumer<MapValue, Measurement>(ProduceMaps, ConsumeMaps);

    private static Measurement MeasureSameThreadListCore(int iterations)
    {
        using var producer = CreateContext(maxListLength: CollectionSize, maxMapEntries: 0);
        using var consumer = CreateContext(maxListLength: CollectionSize, maxMapEntries: 0);
        var source = CreateListSource(producer);
        var one = SandboxValue.FromInt32(1);
        var two = SandboxValue.FromInt32(2);
        long elapsedTicks = 0;
        long allocatedBytes = 0;
        long checksum = 0;

        for (var index = 0; index < iterations; index++)
        {
            var item = (index & 1) == 0 ? one : two;
            var value = (ListValue)CompiledRuntime.ListAdd(producer, source, item);
            MeasureCharge(consumer, value, ref elapsedTicks, ref allocatedBytes);
            checksum += ValidateList(value);
        }

        return Capture(iterations, elapsedTicks, allocatedBytes, checksum, consumer, isList: true);
    }

    private static Measurement MeasureSameThreadMapCore(int iterations)
    {
        using var producer = CreateContext(maxListLength: 0, maxMapEntries: CollectionSize);
        using var consumer = CreateContext(maxListLength: 0, maxMapEntries: CollectionSize);
        var source = CreateMapSource(producer);
        var key = SandboxValue.FromInt32(0);
        var even = SandboxValue.FromInt32(1_000);
        var odd = SandboxValue.FromInt32(1_001);
        long elapsedTicks = 0;
        long allocatedBytes = 0;
        long checksum = 0;

        for (var index = 0; index < iterations; index++)
        {
            var item = (index & 1) == 0 ? even : odd;
            var value = (MapValue)CompiledRuntime.MapSet(producer, source, key, item);
            MeasureCharge(consumer, value, ref elapsedTicks, ref allocatedBytes);
            checksum += ValidateMap(value, key);
        }

        return Capture(iterations, elapsedTicks, allocatedBytes, checksum, consumer, isList: false);
    }

    private static void ProduceLists(SingleValueHandoff<ListValue> handoff)
    {
        using var context = CreateContext(maxListLength: CollectionSize, maxMapEntries: 0);
        var source = CreateListSource(context);
        var items = new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) };
        for (var index = 0; index < WarmupIterations + ListIterations; index++)
        {
            handoff.WaitToProduce();
            handoff.Publish((ListValue)CompiledRuntime.ListAdd(
                context, source, items[index & 1]));
        }
    }

    private static void ProduceMaps(SingleValueHandoff<MapValue> handoff)
    {
        using var context = CreateContext(maxListLength: 0, maxMapEntries: CollectionSize);
        var source = CreateMapSource(context);
        var key = SandboxValue.FromInt32(0);
        var items = new[] { SandboxValue.FromInt32(1_000), SandboxValue.FromInt32(1_001) };
        for (var index = 0; index < WarmupIterations + MapIterations; index++)
        {
            handoff.WaitToProduce();
            handoff.Publish((MapValue)CompiledRuntime.MapSet(
                context, source, key, items[index & 1]));
        }
    }

    private static Measurement ConsumeLists(SingleValueHandoff<ListValue> handoff)
    {
        using (var warmup = CreateContext(maxListLength: CollectionSize, maxMapEntries: 0))
        {
            for (var index = 0; index < WarmupIterations; index++)
            {
                var value = handoff.Take();
                warmup.ChargeValue(value);
                _ = ValidateList(value);
                handoff.Release();
            }
        }

        using var consumer = CreateContext(maxListLength: CollectionSize, maxMapEntries: 0);
        long elapsedTicks = 0;
        long allocatedBytes = 0;
        long checksum = 0;
        for (var index = 0; index < ListIterations; index++)
        {
            var value = handoff.Take();
            MeasureCharge(consumer, value, ref elapsedTicks, ref allocatedBytes);
            checksum += ValidateList(value);
            handoff.Release();
        }

        return Capture(ListIterations, elapsedTicks, allocatedBytes, checksum, consumer, isList: true);
    }

    private static Measurement ConsumeMaps(SingleValueHandoff<MapValue> handoff)
    {
        using var consumer = CreateContext(maxListLength: 0, maxMapEntries: CollectionSize);
        var key = SandboxValue.FromInt32(0);
        for (var index = 0; index < WarmupIterations; index++)
        {
            var value = handoff.Take();
            consumer.ChargeValue(value);
            _ = ValidateMap(value, key);
            handoff.Release();
        }

        using var measuredConsumer = CreateContext(maxListLength: 0, maxMapEntries: CollectionSize);
        long elapsedTicks = 0;
        long allocatedBytes = 0;
        long checksum = 0;
        for (var index = 0; index < MapIterations; index++)
        {
            var value = handoff.Take();
            MeasureCharge(measuredConsumer, value, ref elapsedTicks, ref allocatedBytes);
            checksum += ValidateMap(value, key);
            handoff.Release();
        }

        return Capture(
            MapIterations,
            elapsedTicks,
            allocatedBytes,
            checksum,
            measuredConsumer,
            isList: false);
    }

}
