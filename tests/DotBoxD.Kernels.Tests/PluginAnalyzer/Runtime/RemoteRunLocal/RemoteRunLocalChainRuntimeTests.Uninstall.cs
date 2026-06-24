using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    [Fact]
    public async Task Local_terminal_install_can_be_uninstalled_by_plugin_or_install_id()
    {
        var package = LowerToPackage(RemoteRunLocalSource);

        using (var server = PluginServer.Create(defaultPolicy: ChainPolicy()))
        {
            var kernel = await server.InstallAsync(package);

            Assert.True(server.Kernels.TryGet(kernel.Manifest.PluginId, out var byPlugin));
            Assert.Same(kernel, byPlugin);
            Assert.True(server.Uninstall(kernel.Manifest.PluginId));
            Assert.True(kernel.IsRevoked);
            Assert.False(server.Kernels.TryGet(kernel.Manifest.PluginId, out _));
        }

        using (var server = PluginServer.Create(defaultPolicy: ChainPolicy()))
        using (var session = server.CreateSession())
        {
            var kernel = await session.InstallAsync(package);

            Assert.True(server.Kernels.TryGet(kernel.InstallId, out var byInstall));
            Assert.Same(kernel, byInstall);
            Assert.True(session.Uninstall(kernel.InstallId));
            Assert.True(kernel.IsRevoked);
            Assert.False(server.Kernels.TryGet(kernel.InstallId, out _));
        }
    }
}
