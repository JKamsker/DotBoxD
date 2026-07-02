using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageJsonTests
{
    [Fact]
    public void Export_round_trips_manifest_contract_strings_without_utf16_replacement()
    {
        const string validSurrogatePair = "contract-prefix-\uD83D\uDE00-suffix";
        Assert.Equal(validSurrogatePair, RoundTripManifestContract(validSurrogatePair));

        const string malformedUtf16 = "contract-prefix-\uD800-suffix";
        var package = PackageWithManifestContract(malformedUtf16);
        string? json = null;
        var exportError = Record.Exception(() => json = PluginPackageJsonSerializer.Export(package));
        if (exportError is not null)
        {
            Assert.True(
                exportError.Message.Contains("UTF-16", StringComparison.OrdinalIgnoreCase) ||
                exportError.Message.Contains("surrogate", StringComparison.OrdinalIgnoreCase),
                $"Expected export to name malformed UTF-16 or surrogate text, but got: {exportError.Message}");
            return;
        }

        var imported = PluginPackageJsonSerializer.Import(json!);
        Assert.Equal(malformedUtf16, imported.Manifest.Contract);
    }

    private static string RoundTripManifestContract(string contract)
    {
        var package = PackageWithManifestContract(contract);
        var json = PluginPackageJsonSerializer.Export(package);
        return PluginPackageJsonSerializer.Import(json).Manifest.Contract;
    }

    private static PluginPackage PackageWithManifestContract(string contract)
    {
        var package = PluginPackageJsonSerializer.Import(JsonDamagePackage());
        return package with { Manifest = package.Manifest with { Contract = contract } };
    }
}
