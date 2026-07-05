using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionWireArgumentTypeSurpriseTests
{
    [Fact]
    public async Task Server_extension_wire_argument_kind_mismatch_reports_sandbox_invalid_input()
    {
        using var server = PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());
        var payload = KernelRpcBinaryCodec.EncodeArguments([KernelRpcValue.String("not-a-list")]);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(
            () => kernel.InvokeServerExtensionRpcAsync(payload).AsTask());

        Assert.Equal(SandboxErrorCode.InvalidInput, ex.Error.Code);
        Assert.Contains("server extension", ex.Error.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("List", ex.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Contains("String", ex.Error.SafeMessage, StringComparison.Ordinal);
    }
}
