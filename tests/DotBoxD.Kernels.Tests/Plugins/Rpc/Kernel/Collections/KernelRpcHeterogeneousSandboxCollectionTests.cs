using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcHeterogeneousSandboxCollectionTests
{
    [Fact]
    public void Homogeneous_sandbox_list_round_trips_through_rpc_binary_codec()
    {
        var original = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)]);

        Assert.Equal(SandboxType.List(SandboxType.I32), original.Type);

        var bytes = KernelRpcBinaryCodec.EncodeValue(original);
        var decoded = KernelRpcBinaryCodec.DecodeValue(bytes);
        var restored = KernelRpcValueConverter.ToSandboxValue(decoded, original.Type);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Heterogeneous_sandbox_list_does_not_produce_rpc_bytes()
    {
        var malformed = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromString("two")]);

        Assert.Equal(SandboxType.List(SandboxType.I32), malformed.Type);

        AssertRejectedByBothRoutes(malformed);
    }

    [Fact]
    public void List_with_a_record_that_mismatches_its_declared_type_is_rejected()
    {
        var expectedRecordType = SandboxType.Record([SandboxType.I32, SandboxType.String]);
        var malformedRecord = SandboxValue.FromOwnedRecord(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)]);
        var malformed = SandboxValue.FromOwnedList([malformedRecord], expectedRecordType);

        AssertRejectedByBothRoutes(malformed);
    }

    [Fact]
    public void Map_with_a_record_that_mismatches_its_declared_value_type_is_rejected()
    {
        var expectedRecordType = SandboxType.Record([SandboxType.I32, SandboxType.String]);
        var malformedRecord = SandboxValue.FromOwnedRecord(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)]);
        var malformed = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("key")] = malformedRecord
            },
            SandboxType.String,
            expectedRecordType);

        AssertRejectedByBothRoutes(malformed);
    }

    [Fact]
    public void List_with_an_unknown_item_subtype_is_rejected()
    {
        var malformed = SandboxValue.FromList(
            [new ClaimedI32Value()],
            SandboxType.I32);

        AssertRejectedByBothRoutes(malformed);
    }

    [Fact]
    public void Nested_list_with_null_declared_item_type_is_rejected()
    {
        var malformedItem = new ListValue([], null!);
        var malformed = SandboxValue.FromList(
            [malformedItem],
            SandboxType.List(SandboxType.I32));

        AssertRejectedByBothRoutes(malformed);
    }

    [Fact]
    public void Nested_map_with_null_declared_key_type_is_rejected()
    {
        var malformedItem = new MapValue(
            new Dictionary<SandboxValue, SandboxValue>(),
            null!,
            SandboxType.I32);
        var malformed = SandboxValue.FromList(
            [malformedItem],
            SandboxType.Map(SandboxType.String, SandboxType.I32));

        AssertRejectedByBothRoutes(malformed);
    }

    private static void AssertRejectedByBothRoutes(SandboxValue malformed)
    {
        AssertFailsClosed(
            Record.Exception(() => KernelRpcValueConverter.FromSandboxValue(malformed)),
            "KernelRpcValueConverter.FromSandboxValue");
        AssertFailsClosed(
            Record.Exception(() => KernelRpcBinaryCodec.EncodeValue(malformed)),
            "KernelRpcBinaryCodec.EncodeValue");
    }

    private static void AssertFailsClosed(Exception? exception, string operation)
        => Assert.True(
            exception is ArgumentException or NotSupportedException,
            exception is null
                ? $"{operation} should reject a SandboxValue collection whose declared item type does not match its contents."
                : $"{operation} should reject with ArgumentException or NotSupportedException, but threw {exception.GetType().Name}: {exception.Message}");

    private sealed record ClaimedI32Value : SandboxValue
    {
        public override SandboxType Type => SandboxType.I32;
    }
}
