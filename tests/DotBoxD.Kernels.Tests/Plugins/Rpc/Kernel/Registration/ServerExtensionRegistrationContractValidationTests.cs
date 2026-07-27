namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionRegistrationContractValidationTests
{
    [Fact]
    public async Task RegisterServerExtension_rejects_invalid_service_contract_before_installing_kernel()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var before = server.Kernels.Snapshot().Select(static kernel => kernel.Manifest.PluginId).ToArray();

        var exception = await Record.ExceptionAsync(
            async () => await server
                .RegisterServerExtensionAsync<ConcreteMonsterKillerService, BatchKillerKernel>()
                .AsTask());
        var after = server.Kernels.Snapshot().Select(static kernel => kernel.Manifest.PluginId).ToArray();
        var mappingException = Record.Exception(() => server.ServerExtension<ConcreteMonsterKillerService>());

        Assert.True(
            exception is NotSupportedException &&
            after.SequenceEqual(before) &&
            mappingException is InvalidOperationException,
            "Expected invalid service contract registration to fail before installing a kernel or " +
            "publishing a service mapping. Actual exception: " +
            $"{exception?.GetType().FullName ?? "<none>"}. Before kernels: [{string.Join(", ", before)}]. " +
            $"After kernels: [{string.Join(", ", after)}]. Mapping exception: " +
            $"{mappingException?.GetType().FullName ?? "<none>"}.");
    }

    private sealed class ConcreteMonsterKillerService
    {
        public List<KillResult> KillMonsters(List<int> monsterIds)
            => throw new NotSupportedException();
    }
}
