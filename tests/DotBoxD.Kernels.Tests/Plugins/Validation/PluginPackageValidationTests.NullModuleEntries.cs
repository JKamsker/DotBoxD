using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_null_module_capability_request_entries()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Module = package.Module with
            {
                CapabilityRequests = [.. package.Module.CapabilityRequests, null!]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Message.Contains("capabilityRequests", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("null", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Install_rejects_null_module_function_entries()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Module = package.Module with
            {
                Functions = [.. package.Module.Functions, null!]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Message.Contains("functions", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("null", StringComparison.OrdinalIgnoreCase));
    }
}
