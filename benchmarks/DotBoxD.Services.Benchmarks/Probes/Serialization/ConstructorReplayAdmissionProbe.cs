using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class ConstructorReplayAdmissionProbe
{
    private const int FastValidatorAdmissionCall = 8192;

    public static void Run(MessagePackRpcSerializer serializer)
    {
        WarmCompiler(serializer);

        var exact = new ConstructorReplayActivationDto(42);
        Console.WriteLine("warm-compiler exact-declared admission calls");
        MeasureAdmissionBuckets(serializer, exact);

        ConstructorReplayActivationBaseDto polymorphic = new ConstructorReplayActivationDerivedDto(42);
        Console.WriteLine("warm-compiler runtime-via-base admission calls");
        MeasureAdmissionBuckets(serializer, polymorphic);
    }

    private static void WarmCompiler(MessagePackRpcSerializer serializer)
    {
        var writer = new ArrayBufferWriter<byte>();
        var value = new ConstructorReplayActivationWarmupDto(42);
        for (var i = 0; i <= FastValidatorAdmissionCall; i++)
        {
            serializer.Serialize(writer, value);
            writer.Clear();
        }
    }

    private static void MeasureAdmissionBuckets<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        var expectedWire = ConstructorReplayProbeMeasurement.GetExpectedWire(serializer, value);
        ConstructorReplayProbeMeasurement.ForceGc();

        MeasureCalls(serializer, writer, value, expectedWire, "first call", 1);
        MeasureCalls(serializer, writer, value, expectedWire, "calls 2-128", 127);
        MeasureCalls(serializer, writer, value, expectedWire, "calls 129-4,096", 3968);
        MeasureCalls(serializer, writer, value, expectedWire, "calls 4,097-8,191", 4095);
        MeasureCalls(serializer, writer, value, expectedWire, "call 8,192 admission", 1);
        MeasureCalls(serializer, writer, value, expectedWire, "calls 8,193-16,384", FastValidatorAdmissionCall);
    }

    private static void MeasureCalls<T>(
        MessagePackRpcSerializer serializer,
        ArrayBufferWriter<byte> writer,
        T value,
        ReadOnlySpan<byte> expectedWire,
        string name,
        int calls)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var call = 0; call < calls; call++)
        {
            serializer.Serialize(writer, value);
            checksum += writer.WrittenCount;
            if (call + 1 < calls)
            {
                writer.Clear();
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ConstructorReplayProbeMeasurement.AssertSameWire(name, expectedWire, writer.WrittenSpan);
        var expectedChecksum = checked((long)expectedWire.Length * calls);
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} produced checksum {checksum:N0}; expected {expectedChecksum:N0}.");
        }

        writer.Clear();
        Console.WriteLine(
            $"{name,-24} {elapsed.TotalMicroseconds,9:N1} us " +
            $"{elapsed.TotalNanoseconds / calls,8:N1} ns/op " +
            $"{allocated,12:N0} B {allocated / (double)calls,8:N1} B/op " +
            $"{checksum,8:N0}");
    }
}
