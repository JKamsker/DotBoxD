using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionProxyNullableContractTests
{
    [Fact]
    public async Task Runtime_proxy_rejects_nullable_value_types_in_service_contracts()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        Assert.Throws<NotSupportedException>(
            () => ServerExtensionProxy.Create<INullableEchoService>(kernel));
    }

    private interface INullableEchoService
    {
        int Echo(int? value);
    }
}
