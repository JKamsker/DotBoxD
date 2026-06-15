using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcBinaryCodecTests
{
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
}
