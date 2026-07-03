using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_null_manifest_live_setting_entries()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                LiveSettings = [.. package.Manifest.LiveSettings, null!]
            }
        };

        await AssertInstallValidationDiagnosticAsync(invalid, "liveSettings");
    }

    [Fact]
    public async Task Install_rejects_null_manifest_subscription_entries()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [.. package.Manifest.Subscriptions, null!]
            }
        };

        await AssertInstallValidationDiagnosticAsync(invalid, "subscriptions");
    }

    [Fact]
    public async Task Install_rejects_null_manifest_indexed_predicate_entries()
    {
        var package = FireDamagePluginPackage.Create();
        var subscription = package.Manifest.Subscriptions[0];
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    subscription with
                    {
                        IndexedPredicates = [.. subscription.IndexedPredicates, null!]
                    }
                ]
            }
        };

        await AssertInstallValidationDiagnosticAsync(invalid, "indexedPredicates");
    }

    private static async Task AssertInstallValidationDiagnosticAsync(
        PluginPackage package,
        string collectionName)
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(package).AsTask());

        Assert.Contains(ex.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(collectionName, StringComparison.OrdinalIgnoreCase));
    }
}
