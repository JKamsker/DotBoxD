using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageJsonTests
{
    [Fact]
    public void Export_rejects_null_required_capabilities()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, null!]
            }
        };

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Export(invalid));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "E-JSON-EXPORT" &&
            d.Message.Contains("requiredCapabilities", StringComparison.Ordinal) &&
            d.Message.Contains("null", StringComparison.OrdinalIgnoreCase));
    }
}
