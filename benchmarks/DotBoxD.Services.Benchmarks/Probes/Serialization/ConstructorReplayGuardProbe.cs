using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using MessagePack;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class ConstructorReplayGuardProbe
{
    public static void Run()
    {
        var serializer = new MessagePackRpcSerializer();
        var stable = new ConstructorReplayStableDto(42);
        var derived = new ConstructorReplayDerivedDto(42);
        ConstructorReplayBaseDto derivedViaBase = derived;
        var exactDerivedControl = new ConstructorReplayExactDerivedControlDto(42);
        ConstructorReplayAlternatingBaseDto alternatingFirst =
            new ConstructorReplayAlternatingFirstDto(42);
        ConstructorReplayAlternatingBaseDto alternatingSecond =
            new ConstructorReplayAlternatingSecondDto(42);
        var settable = new ConstructorReplaySettableDto { Id = 42 };
        var complex = new ConstructorReplayComplexDto([1, 2, 3]);
        ConstructorReplayComplexBaseDto complexPolymorphic =
            new ConstructorReplayComplexDerivedDto([1, 2, 3]);

        var stableWire = ConstructorReplayProbeMeasurement.GetExpectedWire(serializer, stable);
        var baseWire = ConstructorReplayProbeMeasurement.GetExpectedWire(serializer, derivedViaBase);
        var exactDerivedWire = ConstructorReplayProbeMeasurement.GetExpectedWire(
            serializer,
            exactDerivedControl);
        var alternatingFirstWire = ConstructorReplayProbeMeasurement.GetExpectedWire(
            serializer,
            alternatingFirst);
        var alternatingSecondWire = ConstructorReplayProbeMeasurement.GetExpectedWire(
            serializer,
            alternatingSecond);
        var settableWire = ConstructorReplayProbeMeasurement.GetExpectedWire(serializer, settable);
        var scalarWire = ConstructorReplayProbeMeasurement.GetExpectedWire(serializer, 42);
        var complexWire = ConstructorReplayProbeMeasurement.GetExpectedWire(serializer, complex);
        var complexPolymorphicWire = ConstructorReplayProbeMeasurement.GetExpectedWire(
            serializer,
            complexPolymorphic);
        ConstructorReplayProbeMeasurement.AssertSameWire(
            "exact-derived control",
            baseWire,
            exactDerivedWire);
        ConstructorReplayProbeMeasurement.AssertSameWire(
            "alternating runtime types",
            alternatingFirstWire,
            alternatingSecondWire);

        Console.WriteLine(
            $"iterations = {ConstructorReplayProbeMeasurement.MeasurementIterations:N0}; " +
            $"warmup = {ConstructorReplayProbeMeasurement.WarmupIterations:N0}");
        Console.WriteLine(
            "case                                    ms      ns/op      allocated B      B/op   checksum");
        Write(Measure(
            "direct MessagePack lower bound",
            writer => MessagePackSerializer.Serialize(writer, stable, serializer.Options),
            stableWire));
        Write(Measure(
            "I32 scalar control",
            writer => serializer.Serialize(writer, 42),
            scalarWire));
        Write(Measure(
            "settable no-guard control",
            writer => serializer.Serialize(writer, settable),
            settableWire));
        Write(Measure(
            "stable constructor exact",
            writer => serializer.Serialize(writer, stable),
            stableWire));
        Write(Measure(
            "exact-derived control",
            writer => serializer.Serialize(writer, exactDerivedControl),
            exactDerivedWire));
        Write(Measure(
            "complex bound DTO",
            writer => serializer.Serialize(writer, complex),
            complexWire));
        Write(Measure(
            "unsupported polymorphic fallback",
            writer => serializer.Serialize(writer, complexPolymorphic),
            complexPolymorphicWire));

        ValidateRoundTrips(
            serializer,
            stable,
            derivedViaBase,
            exactDerivedControl,
            alternatingFirst,
            alternatingSecond,
            settable,
            complex,
            complexPolymorphic);
        Write(Measure(
            "warmed runtime via base",
            writer => serializer.Serialize(writer, derivedViaBase),
            baseWire));
        var alternate = false;
        Write(Measure(
            "two runtime types via one base",
            writer =>
            {
                serializer.Serialize(writer, alternate ? alternatingFirst : alternatingSecond);
                alternate = !alternate;
            },
            alternatingFirstWire));

        ConstructorReplayAdmissionProbe.Run(serializer);
    }

    private static void ValidateRoundTrips(
        MessagePackRpcSerializer serializer,
        ConstructorReplayStableDto stable,
        ConstructorReplayBaseDto derivedViaBase,
        ConstructorReplayExactDerivedControlDto exactDerived,
        ConstructorReplayAlternatingBaseDto alternatingFirst,
        ConstructorReplayAlternatingBaseDto alternatingSecond,
        ConstructorReplaySettableDto settable,
        ConstructorReplayComplexDto complex,
        ConstructorReplayComplexBaseDto complexPolymorphic)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, stable);
        var stableRoundTrip = serializer.Deserialize<ConstructorReplayStableDto>(writer.WrittenMemory);
        if (stableRoundTrip.Id != stable.Id)
        {
            throw new InvalidOperationException("Stable constructor DTO did not round-trip.");
        }

        writer.Clear();
        serializer.Serialize(writer, derivedViaBase);
        var derivedRoundTrip = serializer.Deserialize<ConstructorReplayBaseDto>(writer.WrittenMemory);
        if (derivedRoundTrip.Id != derivedViaBase.Id)
        {
            throw new InvalidOperationException("Base-declared DTO did not round-trip.");
        }

        writer.Clear();
        serializer.Serialize(writer, exactDerived);
        var exactDerivedRoundTrip =
            serializer.Deserialize<ConstructorReplayExactDerivedControlDto>(writer.WrittenMemory);
        if (exactDerivedRoundTrip.Id != exactDerived.Id)
        {
            throw new InvalidOperationException("Exact-derived DTO did not round-trip.");
        }

        ValidateAlternatingRoundTrip(serializer, writer, alternatingFirst, "First alternating DTO");
        ValidateAlternatingRoundTrip(serializer, writer, alternatingSecond, "Second alternating DTO");

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

        writer.Clear();
        serializer.Serialize(writer, complexPolymorphic);
        var complexPolymorphicRoundTrip =
            serializer.Deserialize<ConstructorReplayComplexBaseDto>(writer.WrittenMemory);
        if (!complexPolymorphicRoundTrip.Values.SequenceEqual(complexPolymorphic.Values))
        {
            throw new InvalidOperationException("Complex polymorphic DTO did not round-trip.");
        }
    }

    private static void ValidateAlternatingRoundTrip(
        MessagePackRpcSerializer serializer,
        ArrayBufferWriter<byte> writer,
        ConstructorReplayAlternatingBaseDto value,
        string name)
    {
        writer.Clear();
        serializer.Serialize(writer, value);
        var roundTrip =
            serializer.Deserialize<ConstructorReplayAlternatingBaseDto>(writer.WrittenMemory);
        if (roundTrip.Id != value.Id)
        {
            throw new InvalidOperationException($"{name} did not round-trip.");
        }
    }

    private static ConstructorReplayProbeMeasurement.Measurement Measure(
        string name,
        Action<ArrayBufferWriter<byte>> serialize,
        ReadOnlyMemory<byte> expectedWire)
        => ConstructorReplayProbeMeasurement.Measure(name, serialize, expectedWire);

    private static void Write(ConstructorReplayProbeMeasurement.Measurement measurement)
        => ConstructorReplayProbeMeasurement.Write(measurement);
}
