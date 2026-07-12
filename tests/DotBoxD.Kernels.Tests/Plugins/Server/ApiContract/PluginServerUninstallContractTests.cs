using DotBoxD.Plugins;

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
}
