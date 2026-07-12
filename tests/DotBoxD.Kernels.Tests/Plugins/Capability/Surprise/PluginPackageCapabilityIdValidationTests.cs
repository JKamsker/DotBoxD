using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Theory]
    [InlineData("event.read.bad..id")]
    [InlineData(".event.read.bad.id")]
    [InlineData("event.read.bad.id.")]
    public async Task Install_rejects_empty_segment_event_read_required_capability_entries(string malformedCapability)
    {
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

        Assert.Contains(ex.Diagnostics, d => MatchesMalformedRequiredCapability(
            d,
            "Plugin manifest",
            malformedCapability));
        Assert.Contains(ex.Diagnostics, d => MatchesMalformedRequiredCapability(
            d,
            "Plugin module metadata",
            malformedCapability));
    }

    private static bool MatchesMalformedRequiredCapability(
        SandboxDiagnostic diagnostic,
        string source,
        string malformedCapability)
        => diagnostic.Code == "DBXK052" &&
           diagnostic.Message.Contains(source, StringComparison.Ordinal) &&
           diagnostic.Message.Contains("requiredCapabilities", StringComparison.Ordinal) &&
           diagnostic.Message.Contains("capability id", StringComparison.Ordinal) &&
           diagnostic.Message.Contains(malformedCapability, StringComparison.Ordinal);
}
