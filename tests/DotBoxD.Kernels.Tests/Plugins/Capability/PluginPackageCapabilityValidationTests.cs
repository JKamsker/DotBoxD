using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_empty_event_read_required_capability_entries()
    {
        var policy = PluginAddendumTestPolicies.LongWall();
        policy = policy with
        {
            Grants = [.. policy.Grants, new CapabilityGrant("event.read.", new Dictionary<string, string>())]
        };
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: policy);
        var package = FireDamagePluginPackage.Create();
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["requiredCapabilities"] = "event.read."
        };
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, "event.read."]
            },
            Module = package.Module with { Metadata = metadata }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK052" &&
            d.Message.Contains("requiredCapabilities", StringComparison.Ordinal) &&
            d.Message.Contains("event.read.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_rejects_wildcard_event_read_required_capability_entries()
    {
        var policy = PluginAddendumTestPolicies.LongWall();
        policy = policy with
        {
            Grants = [.. policy.Grants, new CapabilityGrant("event.read.*", new Dictionary<string, string>())]
        };
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: policy);
        var package = FireDamagePluginPackage.Create();
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["requiredCapabilities"] = "event.read.*"
        };
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, "event.read.*"]
            },
            Module = package.Module with { Metadata = metadata }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK051" &&
            d.Message.Contains("requiredCapabilities", StringComparison.Ordinal) &&
            d.Message.Contains("event.read.*", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_rejects_manifest_required_capabilities_even_when_module_metadata_self_asserts_them()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["requiredCapabilities"] = "file.write"
        };
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, "file.write"]
            },
            Module = package.Module with { Metadata = metadata }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK044");
    }

    [Fact]
    public async Task Install_rejects_null_manifest_required_capability_entries()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, null!]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK045" &&
            d.Message.Contains("requiredCapabilities", StringComparison.Ordinal));
    }
}
