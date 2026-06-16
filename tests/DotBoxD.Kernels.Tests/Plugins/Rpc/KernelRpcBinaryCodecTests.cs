using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcBinaryCodecTests
{
    private const int MaxDecodeDepth = 64;

    [Fact]
    public void DecodeValue_rejects_length_prefix_that_overflows_int()
    {
        var payload = new byte[]
        {
            (byte)KernelRpcValueKind.String,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0x08
        };

        var ex = Assert.Throws<FormatException>(() => KernelRpcBinaryCodec.DecodeValue(payload));
        Assert.Contains("invalid length prefix", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeValue_allows_payload_at_maximum_depth()
    {
        var value = KernelRpcBinaryCodec.DecodeValue(NestedListPayload(MaxDecodeDepth));

        for (var i = 0; i < MaxDecodeDepth; i++)
        {
            value.RequireKind(KernelRpcValueKind.List);
            value = Assert.Single(value.Items);
        }

        value.RequireKind(KernelRpcValueKind.Unit);
    }

    [Fact]
    public void DecodeValue_rejects_payload_past_maximum_depth()
    {
        var ex = Assert.Throws<FormatException>(
            () => KernelRpcBinaryCodec.DecodeValue(NestedListPayload(MaxDecodeDepth + 1)));

        Assert.Contains("nesting depth", ex.Message, StringComparison.Ordinal);
    }

    private static byte[] NestedListPayload(int depth)
    {
        var bytes = new List<byte>((depth * 2) + 1);
        for (var i = 0; i < depth; i++)
        {
            bytes.Add((byte)KernelRpcValueKind.List);
            bytes.Add(1);
        }

        bytes.Add((byte)KernelRpcValueKind.Unit);
        return bytes.ToArray();
    }
}
