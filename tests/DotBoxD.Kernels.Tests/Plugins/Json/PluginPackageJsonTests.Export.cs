using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
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

    [Fact]
    public void Export_rejects_unsupported_manifest_effects()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Effects = [.. package.Manifest.Effects, "Bogus"]
            }
        };

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Export(invalid));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK040");
    }

    [Fact]
    public void Export_rejects_duplicate_manifest_effects()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Effects = [.. package.Manifest.Effects, package.Manifest.Effects[0]]
            }
        };

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Export(invalid));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK040" &&
            d.Message.Contains("declared more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_rejects_live_setting_defaults_outside_manifest_range()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                LiveSettings =
                [
                    new LiveSettingDefinition("MinDamage", "int", 10001, 0, 10000)
                ]
            }
        };

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Export(invalid));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK023");
    }
}
