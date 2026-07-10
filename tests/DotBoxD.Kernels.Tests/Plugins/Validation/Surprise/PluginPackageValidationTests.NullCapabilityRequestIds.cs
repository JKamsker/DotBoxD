using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_null_module_capability_request_ids_as_structural_validation()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Module = package.Module with
            {
                CapabilityRequests =
                [
                    .. package.Module.CapabilityRequests,
                    new CapabilityRequest(null!, "malformed public request")
                ]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await DotBoxD.Plugins.PluginServer
                .Create(defaultPolicy: PluginAddendumTestPolicies.LongWall())
                .InstallAsync(invalid)
                .AsTask());

        Assert.Contains(ex.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("capability id", StringComparison.OrdinalIgnoreCase));
    }
}
