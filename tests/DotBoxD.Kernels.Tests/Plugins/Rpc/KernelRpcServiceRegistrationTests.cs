namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcServiceRegistrationTests
{
    [Fact]
    public async Task Uninstall_clears_rpc_service_registration()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        await server.RegisterRpcServiceAsync<IMonsterKillerService, BatchKillerKernel>();

        Assert.True(server.Uninstall("monster-killer"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => server.RpcService<IMonsterKillerService>());
        Assert.Contains("No kernel RPC service is registered", ex.Message);
    }
}
