using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class KernelRpcResponseEncodingAllocationTests
{
    private const int WarmupIterations = 1_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public void Nested_response_avoids_materialized_types_and_the_intermediate_wire_tree()
    {
        var recordType = SandboxType.Record([SandboxType.I32, SandboxType.String]);
        var response = CreateRecordList(8, recordType);
        var record = ((ListValue)response).Values[0];
        var matchState = new MatchState(record, recordType);

        _ = MeasureMatches(matchState, LegacyMatches, WarmupIterations);
        _ = MeasureMatches(matchState, ExactMatches, WarmupIterations);
        _ = MeasureEncode(response, LegacyEncode, WarmupIterations);
        _ = MeasureEncode(response, DirectEncode, WarmupIterations);

        var materializedMatch = MeasureMatches(
            matchState,
            LegacyMatches,
            MeasurementIterations);
        var exactMatch = MeasureMatches(
            matchState,
            ExactMatches,
            MeasurementIterations);
        var legacyEncode = MeasureEncode(
            response,
            LegacyEncode,
            MeasurementIterations);
        var directEncode = MeasureEncode(
            response,
            DirectEncode,
            MeasurementIterations);

        Assert.Equal(136L * MeasurementIterations, materializedMatch.Bytes);
        Assert.Equal(0, exactMatch.Bytes);
        Assert.Equal(1_720L * MeasurementIterations, legacyEncode.Bytes);
        Assert.Equal(160L * MeasurementIterations, directEncode.Bytes);
        Assert.Equal(1_560L * MeasurementIterations, legacyEncode.Bytes - directEncode.Bytes);
        Assert.Equal(MeasurementIterations, materializedMatch.Checksum);
        Assert.Equal(MeasurementIterations, exactMatch.Checksum);
        Assert.Equal(98L * MeasurementIterations, legacyEncode.Checksum);
        Assert.Equal(legacyEncode.Checksum, directEncode.Checksum);
    }

    private static Measurement MeasureMatches(
        MatchState state,
        Func<MatchState, bool> matches,
        int iterations)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            checksum += matches(state) ? 1 : 0;
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static Measurement MeasureEncode(
        SandboxValue value,
        Func<SandboxValue, byte[]> encode,
        int iterations)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            checksum += encode(value).Length;
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static bool LegacyMatches(MatchState state)
        => state.Value.Type.Equals(state.ExpectedType);

    private static bool ExactMatches(MatchState state)
        => SandboxValueTypeMatcher.MatchesExactType(state.Value, state.ExpectedType);

    private static byte[] LegacyEncode(SandboxValue value)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValueConverter.FromSandboxValue(value));

    private static byte[] DirectEncode(SandboxValue value)
        => KernelRpcBinaryCodec.EncodeValue(value);

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

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct MatchState(SandboxValue Value, SandboxType ExpectedType);

    private readonly record struct Measurement(long Bytes, long Checksum);
}
