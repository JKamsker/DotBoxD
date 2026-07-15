using System.Buffers.Binary;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class KernelRpcPayloadReaderSkipValueTests
{
    private const int MaxDecodeDepth = 64;
    private const int MaxDecodeItems = 10_000;

    public static TheoryData<byte[]> MalformedPayloads()
        => new()
        {
            { [(byte)KernelRpcValueKind.Bool, 2] },
            { [(byte)KernelRpcValueKind.String, 2, 0xC3, 0x28] },
            { F64Payload(double.NaN) },
            { [(byte)KernelRpcValueKind.Map, 1, (byte)KernelRpcValueKind.Unit] },
            { NestedListPayload(MaxDecodeDepth + 1) },
            { [(byte)KernelRpcValueKind.List, .. LengthPrefix(MaxDecodeItems + 1)] },
            { CumulativeItemLimitPayload() },
            { [(byte)KernelRpcValueKind.List, 0x80, 0x80, 0x80, 0x80, 0x80] },
            { [byte.MaxValue] },
            { [(byte)KernelRpcValueKind.I64] },
            { [(byte)KernelRpcValueKind.Unit, (byte)KernelRpcValueKind.Unit] }
        };

    public static TheoryData<byte[]> ValidScalarPayloads()
        => new()
        {
            { KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Bool(true)) },
            { KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int64(long.MinValue)) },
            { KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Double(123.5)) },
            { KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Guid(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"))) }
        };

    public static TheoryData<byte[]> OrderedMapFailures()
        => new()
        {
            { [(byte)KernelRpcValueKind.Map, 1] },
            { [(byte)KernelRpcValueKind.Map, 1, byte.MaxValue] }
        };

    [Theory]
    [MemberData(nameof(ValidScalarPayloads))]
    public void SkipValue_accepts_each_scalar_wire_kind(byte[] payload)
        => SkipAndRequireConsumed(payload);

    [Fact]
    public void SkipValue_allows_payload_at_maximum_depth()
        => SkipAndRequireConsumed(NestedListPayload(MaxDecodeDepth));

    [Theory]
    [MemberData(nameof(MalformedPayloads))]
    public void SkipValue_rejects_the_same_malformed_wire_payloads_as_DecodeValue(byte[] payload)
    {
        var legacy = Record.Exception(() => KernelRpcBinaryCodec.DecodeValue(payload));
        var skipped = Record.Exception(() => SkipAndRequireConsumed(payload));

        Assert.NotNull(legacy);
        Assert.IsType(legacy.GetType(), skipped);
    }

    [Theory]
    [MemberData(nameof(OrderedMapFailures))]
    public void SkipValue_preserves_legacy_map_failure_precedence(byte[] payload)
    {
        var legacy = Assert.Throws<FormatException>(() => KernelRpcBinaryCodec.DecodeValue(payload));
        var skipped = Assert.Throws<FormatException>(() => SkipAndRequireConsumed(payload));

        Assert.Equal(legacy.Message, skipped.Message);
    }

    [Fact]
    public void SkipValue_consumes_exactly_one_value()
    {
        var first = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(
            [KernelRpcValue.Int32(1), KernelRpcValue.String("first")]));
        var second = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42));
        var payload = first.Concat(second).ToArray();
        var reader = new KernelRpcPayloadReader(payload);

        reader.SkipValue();
        var result = reader.ReadInt32();
        reader.EnsureConsumed();

        Assert.Equal(42, result);
    }

    [Fact]
    public void SkipValue_validates_nested_payload_without_allocating()
    {
        var payload = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Map(
        [
            KernelRpcValue.String("items"),
            KernelRpcValue.List(
            [
                KernelRpcValue.Record([KernelRpcValue.Int32(1), KernelRpcValue.String("one")]),
                KernelRpcValue.Record([KernelRpcValue.Int32(2), KernelRpcValue.String("two")])
            ])
        ]));
        for (var i = 0; i < 1_000; i++)
        {
            SkipAndRequireConsumed(payload);
        }

        ForceGc();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20_000; i++)
        {
            SkipAndRequireConsumed(payload);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void SkipAndRequireConsumed(byte[] payload)
    {
        var reader = new KernelRpcPayloadReader(payload);
        reader.SkipValue();
        reader.EnsureConsumed();
    }

    private static byte[] NestedListPayload(int depth)
    {
        var bytes = new byte[(depth * 2) + 1];
        for (var i = 0; i < depth; i++)
        {
            bytes[i * 2] = (byte)KernelRpcValueKind.List;
            bytes[(i * 2) + 1] = 1;
        }

        bytes[^1] = (byte)KernelRpcValueKind.Unit;
        return bytes;
    }

    private static byte[] LengthPrefix(int value)
    {
        var bytes = new List<byte>();
        var remaining = (uint)value;
        while (remaining >= 0x80)
        {
            bytes.Add((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }

        bytes.Add((byte)remaining);
        return bytes.ToArray();
    }

    private static byte[] F64Payload(double value)
    {
        var payload = new byte[sizeof(byte) + sizeof(long)];
        payload[0] = (byte)KernelRpcValueKind.F64;
        BinaryPrimitives.WriteInt64LittleEndian(
            payload.AsSpan(1),
            BitConverter.DoubleToInt64Bits(value));
        return payload;
    }

    private static byte[] CumulativeItemLimitPayload()
    {
        var payload = new List<byte>
        {
            (byte)KernelRpcValueKind.List,
            2,
            (byte)KernelRpcValueKind.List
        };
        payload.AddRange(LengthPrefix(5_000));
        payload.AddRange(Enumerable.Repeat((byte)KernelRpcValueKind.Unit, 5_000));
        payload.Add((byte)KernelRpcValueKind.List);
        payload.AddRange(LengthPrefix(5_001));
        payload.AddRange(Enumerable.Repeat((byte)KernelRpcValueKind.Unit, 5_001));
        return payload.ToArray();
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
