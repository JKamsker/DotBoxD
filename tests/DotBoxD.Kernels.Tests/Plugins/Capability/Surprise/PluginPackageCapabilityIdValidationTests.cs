using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_empty_segment_event_read_required_capability_entries()
    {
        const string malformedCapability = "event.read.bad..id";
        var policy = PluginAddendumTestPolicies.LongWall();
        policy = policy with
        {
            Grants = [.. policy.Grants, new CapabilityGrant(malformedCapability, new Dictionary<string, string>())]
        };
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: policy);
        var package = FireDamagePluginPackage.Create();
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["requiredCapabilities"] = malformedCapability
        };
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, malformedCapability]
            },
            Module = package.Module with { Metadata = metadata }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Message.Contains("requiredCapabilities", StringComparison.Ordinal) &&
            d.Message.Contains("capability id", StringComparison.Ordinal) &&
            d.Message.Contains(malformedCapability, StringComparison.Ordinal));
    }
}
