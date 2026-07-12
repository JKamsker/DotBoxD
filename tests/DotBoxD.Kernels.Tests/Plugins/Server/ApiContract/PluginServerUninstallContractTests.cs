using DotBoxD.Plugins;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginServerUninstallContractTests
{
    [Fact]
    public void Uninstall_rejects_null_plugin_id_with_public_parameter_name()
    {
        using var server = PluginServer.Create();

        var exception = Assert.Throws<ArgumentNullException>(() => server.Uninstall(null!));

        Assert.Equal("pluginId", exception.ParamName);
    }

    [Fact]
    public void Uninstall_returns_false_for_missing_plugin_id()
    {
        using var server = PluginServer.Create();

        Assert.False(server.Uninstall("missing-plugin"));
    }

    [Fact]
    public async Task Uninstall_returns_true_and_revokes_installed_kernel()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        Assert.True(server.Uninstall("fire-damage"));
        Assert.True(kernel.IsRevoked);
        Assert.False(server.Uninstall("fire-damage"));
    }
}
