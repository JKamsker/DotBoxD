using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class KernelRpcResponseEncodingProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 200_000;
    private const int MatchIterations = 1_000_000;

    public static void Run()
    {
        var scalar = SandboxValue.FromInt32(42);
        var list = CreateInt32List(3);
        var map = CreateMap(32);
        var recordType = SandboxType.Record([SandboxType.I32, SandboxType.String]);
        var nested = CreateRecordList(8, recordType);
        var record = ((ListValue)nested).Values[0];

        Console.WriteLine($"declared type matches = {MatchIterations:N0}");
        Console.WriteLine("case                         total ms    allocated B       B/op   checksum");
        RunMatchCase("scalar I32", scalar, SandboxType.I32);
        RunMatchCase("List<I32>", list, SandboxType.List(SandboxType.I32));
        RunMatchCase("Map<String,I32>", map, SandboxType.Map(SandboxType.String, SandboxType.I32));
        RunMatchCase("Record<I32,String>", record, recordType);

        AssertByteParity(scalar);
        AssertByteParity(list);
        AssertByteParity(map);
        AssertByteParity(nested);
        Warmup(scalar);
        Warmup(list);
        Warmup(map);
        Warmup(nested);

        Console.WriteLine($"kernel RPC response encodes = {Iterations:N0}");
        Console.WriteLine("case                         total ms    allocated B       B/op   checksum");
        RunCase("scalar I32", scalar);
        RunCase("List<I32>, 3 items", list);
        RunCase("Map<String,I32>, 32", map);
        RunCase("List<Record>, 8 items", nested);
    }

    private static void RunMatchCase(string name, SandboxValue value, SandboxType expectedType)
    {
        var state = new MatchState(value, expectedType);
        _ = MeasureMatch(state, LegacyMatches, WarmupIterations);
        _ = MeasureMatch(state, ExactMatches, WarmupIterations);
        Write(name + " type legacy", MeasureMatch(state, LegacyMatches, MatchIterations));
        Write(name + " type exact", MeasureMatch(state, ExactMatches, MatchIterations));
    }

    private static void RunCase(string name, SandboxValue value)
    {
        Write(name + " legacy", Measure(value, LegacyEncode));
        Write(name + " direct", Measure(value, DirectEncode));
    }

    private static void Warmup(SandboxValue value)
    {
        _ = Measure(value, LegacyEncode, WarmupIterations);
        _ = Measure(value, DirectEncode, WarmupIterations);
    }

    private static Measurement Measure(
        SandboxValue value,
        Func<SandboxValue, byte[]> encode,
        int iterations = Iterations)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum += encode(value).Length;
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static Measurement MeasureMatch(
        MatchState state,
        Func<MatchState, bool> matches,
        int iterations)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum += matches(state) ? 1 : 0;
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static bool LegacyMatches(MatchState state)
        => state.Value.Type.Equals(state.ExpectedType);

    private static bool ExactMatches(MatchState state)
        => SandboxValueTypeMatcher.MatchesExactType(state.Value, state.ExpectedType);

    private static byte[] LegacyEncode(SandboxValue value)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValueConverter.FromSandboxValue(value));

    private static byte[] DirectEncode(SandboxValue value)
        => KernelRpcBinaryCodec.EncodeValue(value);

    private static void AssertByteParity(SandboxValue value)
    {
        if (!LegacyEncode(value).AsSpan().SequenceEqual(DirectEncode(value)))
        {
            throw new InvalidOperationException("direct sandbox response encoding changed the wire payload");
        }
    }

    private static SandboxValue CreateInt32List(int count)
    {
        var values = new SandboxValue[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = SandboxValue.FromInt32(i);
        }

        return SandboxValue.FromOwnedList(values, SandboxType.I32);
    }

    private static SandboxValue CreateMap(int count)
    {
        var values = new Dictionary<SandboxValue, SandboxValue>(count);
        for (var i = 0; i < count; i++)
        {
            values.Add(SandboxValue.FromString($"key-{i}"), SandboxValue.FromInt32(i));
        }

        return SandboxValue.FromMap(values, SandboxType.String, SandboxType.I32);
    }

    private static SandboxValue CreateRecordList(int count, SandboxType recordType)
    {
        var records = new SandboxValue[count];
        for (var i = 0; i < count; i++)
        {
            records[i] = SandboxValue.FromOwnedRecord(
                [SandboxValue.FromInt32(i), SandboxValue.FromString("tag")]);
        }

        return SandboxValue.FromOwnedList(records, recordType);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.Milliseconds,8:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.BytesPerOperation,10:N1} {measurement.Checksum,10:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        long Checksum,
        int Iterations)
    {
        public double BytesPerOperation => (double)AllocatedBytes / Iterations;
    }

    private readonly record struct MatchState(SandboxValue Value, SandboxType ExpectedType);
}
