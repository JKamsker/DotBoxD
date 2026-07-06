using DotBoxD.Kernels.Sandbox;
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
    public void Heterogeneous_sandbox_list_is_rejected_before_rpc_bytes_are_written()
    {
        var malformed = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromString("two")]);

        Assert.Equal(SandboxType.List(SandboxType.I32), malformed.Type);

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
}
