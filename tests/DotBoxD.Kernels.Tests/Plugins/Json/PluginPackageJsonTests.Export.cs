using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageJsonTests
{
    [Fact]
    public void Export_rejects_undefined_manifest_execution_mode()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Mode = (ExecutionMode)999 } };

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Export(invalid));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK042");
    }
}
