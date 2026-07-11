using System.Collections;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.ServerExtension.Revocation;

public sealed class ServerExtensionProxyRevocationPayloadTests
{
    [Fact]
    public async Task Retained_proxy_rejects_revoked_kernel_before_payload_enumeration()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            ServerExtensionProxyTests.MonsterKillerWithGeneratedClientSource,
            "Sample.MonsterKillerPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IRevokedEnumerableMonsterKillerService>(kernel);
        var enumerations = 0;

        kernel.Revoke();
        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(
            async () => await service.KillMonstersAsync(new TrackingEnumerable(() => enumerations++)));

        Assert.Equal(SandboxErrorCode.PolicyDenied, ex.Error.Code);
        Assert.Equal(0, enumerations);
    }

    private interface IRevokedEnumerableMonsterKillerService
    {
        ValueTask<List<KillResult>> KillMonstersAsync(IEnumerable<int> monsterIds);
    }

    private sealed class TrackingEnumerable(Action onEnumerated) : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator()
        {
            onEnumerated();
            yield return 4;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
