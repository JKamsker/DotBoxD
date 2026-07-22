using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using MessagePack;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class ConstructorReplayGuardProbe
{
    private const int FastValidatorAdmissionCall = 8192;
    private const int WarmupIterations = 100_000;
    private const int MeasurementIterations = 1_000_000;

    public static void Run()
    {
        var serializer = new MessagePackRpcSerializer();
        MeasureColdActivation(serializer);
        var stable = new ConstructorReplayStableDto(42);
        ConstructorReplayBaseDto derived = new ConstructorReplayDerivedDto(42);
        var settable = new ConstructorReplaySettableDto { Id = 42 };
        var complex = new ConstructorReplayComplexDto([1, 2, 3]);

        ValidateRoundTrips(serializer, stable, derived, settable, complex);

        Console.WriteLine(
            $"iterations = {MeasurementIterations:N0}; warmup = {WarmupIterations:N0}");
        Console.WriteLine("case                              ms      ns/op      allocated B      B/op checksum");
        Write(Measure("stable constructor exact", writer => serializer.Serialize(writer, stable)));
        Write(Measure("derived runtime via base", writer => serializer.Serialize(writer, derived)));
        Write(Measure("settable no-guard control", writer => serializer.Serialize(writer, settable)));
        Write(Measure("I32 scalar control", writer => serializer.Serialize(writer, 42)));
        Write(Measure("complex bound DTO", writer => serializer.Serialize(writer, complex)));
        Write(Measure(
            "direct MessagePack lower bound",
            writer => MessagePackSerializer.Serialize(writer, stable, serializer.Options)));
    }

    private static void MeasureColdActivation(MessagePackRpcSerializer serializer)
    {
        var writer = new ArrayBufferWriter<byte>();
        var warmup = new ConstructorReplayActivationWarmupDto(42);
        for (var i = 0; i <= FastValidatorAdmissionCall; i++)
        {
            serializer.Serialize(writer, warmup);
            writer.Clear();
        }

        var value = new ConstructorReplayActivationDto(42);
        ForceGc();
        Console.WriteLine("cold activation calls");
        MeasureColdCalls(serializer, writer, value, "first call", 1);
        MeasureColdCalls(serializer, writer, value, "calls 2-128", 127);
        MeasureColdCalls(serializer, writer, value, "calls 129-4,096", 3968);
        MeasureColdCalls(serializer, writer, value, "calls 4,097-8,191", 4095);
        MeasureColdCalls(serializer, writer, value, "call 8,192 activate", 1);
        MeasureColdCalls(
            serializer,
            writer,
            value,
            "calls 8,193-16,384",
            FastValidatorAdmissionCall);
    }

    private static void MeasureColdCalls(
        MessagePackRpcSerializer serializer,
        ArrayBufferWriter<byte> writer,
        ConstructorReplayActivationDto value,
        string name,
        int calls)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var call = 0; call < calls; call++)
        {
            serializer.Serialize(writer, value);
            writer.Clear();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            $"{name,-18} {elapsed.TotalMicroseconds,9:N1} us " +
            $"{elapsed.TotalNanoseconds / calls,8:N1} ns/op " +
            $"{allocated,12:N0} B {allocated / (double)calls,8:N1} B/op");
    }

    private static Measurement Measure(
        string name,
        Action<ArrayBufferWriter<byte>> serialize)
    {
        var writer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < WarmupIterations; i++)
        {
            serialize(writer);
            writer.Clear();
        }

        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            serialize(writer);
            checksum += writer.WrittenCount;
            writer.Clear();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (checksum <= 0 || checksum % MeasurementIterations != 0)
        {
            throw new InvalidOperationException($"{name} produced an invalid checksum {checksum:N0}.");
        }

        return new Measurement(name, elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static void ValidateRoundTrips(
        MessagePackRpcSerializer serializer,
        ConstructorReplayStableDto stable,
        ConstructorReplayBaseDto derived,
        ConstructorReplaySettableDto settable,
        ConstructorReplayComplexDto complex)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, stable);
        var stableRoundTrip = serializer.Deserialize<ConstructorReplayStableDto>(writer.WrittenMemory);
        if (stableRoundTrip.Id != stable.Id)
        {
            throw new InvalidOperationException("Stable constructor DTO did not round-trip.");
        }

        writer.Clear();
        serializer.Serialize(writer, derived);
        var derivedRoundTrip = serializer.Deserialize<ConstructorReplayBaseDto>(writer.WrittenMemory);
        if (derivedRoundTrip.Id != derived.Id)
        {
            throw new InvalidOperationException("Base-declared DTO did not round-trip.");
        }

        writer.Clear();
        serializer.Serialize(writer, settable);
        var settableRoundTrip = serializer.Deserialize<ConstructorReplaySettableDto>(writer.WrittenMemory);
        if (settableRoundTrip.Id != settable.Id)
        {
            throw new InvalidOperationException("Settable DTO did not round-trip.");
        }

        writer.Clear();
        serializer.Serialize(writer, complex);
        var complexRoundTrip = serializer.Deserialize<ConstructorReplayComplexDto>(writer.WrittenMemory);
        if (!complexRoundTrip.Values.SequenceEqual(complex.Values))
        {
            throw new InvalidOperationException("Complex constructor DTO did not round-trip.");
        }
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-30} {measurement.Milliseconds,8:N1} " +
            $"{measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.AllocatedBytes,16:N0} " +
            $"{measurement.BytesPerOperation,9:N1} {measurement.Checksum,8:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        long Checksum)
    {
        public double NanosecondsPerOperation =>
            Milliseconds * 1_000_000 / MeasurementIterations;

        public double BytesPerOperation =>
            AllocatedBytes / (double)MeasurementIterations;
    }
}

public sealed class ConstructorReplayStableDto
{
    public ConstructorReplayStableDto(int id) => Id = id;

    public int Id { get; }
}

public sealed class ConstructorReplayActivationDto
{
    public ConstructorReplayActivationDto(int id) => Id = id;

    public int Id { get; }
}

public sealed class ConstructorReplayActivationWarmupDto
{
    public ConstructorReplayActivationWarmupDto(int id) => Id = id;

    public int Id { get; }
}

public class ConstructorReplayBaseDto
{
    public ConstructorReplayBaseDto(int id) => Id = id;

    public int Id { get; }
}

public sealed class ConstructorReplayDerivedDto : ConstructorReplayBaseDto
{
    public ConstructorReplayDerivedDto(int id)
        : base(id)
    {
    }
}

public sealed class ConstructorReplaySettableDto
{
    public int Id { get; set; }
}

public sealed class ConstructorReplayComplexDto
{
    public ConstructorReplayComplexDto(int[] values) => Values = values;

    public int[] Values { get; }
}
