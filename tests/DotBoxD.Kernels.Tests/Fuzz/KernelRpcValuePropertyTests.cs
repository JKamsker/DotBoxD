using CsCheck;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Fuzz;

public sealed class KernelRpcValuePropertyTests
{
    private static readonly Gen<KernelRpcValue> Values = Gen.Recursive<KernelRpcValue>((depth, value) =>
    {
        var scalar = Gen.OneOf(
            Gen.Const(KernelRpcValue.Unit()),
            Gen.Bool.Select(KernelRpcValue.Bool),
            Gen.Int.Select(KernelRpcValue.Int32),
            Gen.Long.Select(KernelRpcValue.Int64),
            Gen.OneOf(Gen.Const(double.MinValue), Gen.Const(double.MaxValue), Gen.Int.Select(i => i / 3d))
                .Select(KernelRpcValue.Double),
            Gen.String[Gen.Char.AlphaNumeric, 0, 32].Select(KernelRpcValue.String),
            Gen.Guid.Select(KernelRpcValue.Guid));

        if (depth >= 3)
        {
            return scalar;
        }

        var entries = Gen.Select(value, value, (key, item) => new[] { key, item })
            .Array[0, 4]
            .Select(pairs => pairs.SelectMany(pair => pair).ToArray());
        return Gen.OneOf(
            scalar,
            value.Array[0, 5].Select(KernelRpcValue.List),
            value.Array[0, 5].Select(KernelRpcValue.Record),
            entries.Select(KernelRpcValue.Map));
    });

    [Fact]
    public void Seeded_codec_roundtrip_preserves_all_value_shapes()
        => Values.Sample(value =>
        {
            var decoded = KernelRpcBinaryCodec.DecodeValue(KernelRpcBinaryCodec.EncodeValue(value));
            AssertEquivalent(value, decoded);
        }, seed: "0N0XIzNsQ0O2", iter: 250, threads: 1);

    private static void AssertEquivalent(KernelRpcValue expected, KernelRpcValue actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        switch (expected.Kind)
        {
            case KernelRpcValueKind.Bool:
                Assert.Equal(expected.BoolValue, actual.BoolValue);
                break;
            case KernelRpcValueKind.I32:
                Assert.Equal(expected.Int32Value, actual.Int32Value);
                break;
            case KernelRpcValueKind.I64:
                Assert.Equal(expected.Int64Value, actual.Int64Value);
                break;
            case KernelRpcValueKind.F64:
                Assert.Equal(expected.DoubleValue, actual.DoubleValue);
                break;
            case KernelRpcValueKind.String:
                Assert.Equal(expected.TextValue, actual.TextValue);
                break;
            case KernelRpcValueKind.Guid:
                Assert.Equal(expected.GuidValue, actual.GuidValue);
                break;
            case KernelRpcValueKind.List or KernelRpcValueKind.Record or KernelRpcValueKind.Map:
                Assert.Equal(expected.ItemCount, actual.ItemCount);
                for (var i = 0; i < expected.ItemCount; i++)
                {
                    AssertEquivalent(expected.GetItem(i), actual.GetItem(i));
                }

                break;
        }
    }
}
