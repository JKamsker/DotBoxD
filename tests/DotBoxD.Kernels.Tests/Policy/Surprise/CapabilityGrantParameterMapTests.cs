using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class CapabilityGrantParameterMapTests
{
    [Fact]
    public async Task Prepare_rejects_null_capability_grant_parameter_maps_with_policy_diagnostic()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var grant = new CapabilityGrant("log.write", new Dictionary<string, string>())
        {
            Parameters = null!
        };
        var policy = new SandboxPolicy(
            "null-grant-parameters",
            SandboxEffects.Pure,
            [grant],
            new ResourceLimits(MaxFuel: 5_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, diagnostic =>
            diagnostic.Code == "E-POLICY-GRANT-PARAM" &&
            diagnostic.Message.Contains("parameter", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("map", StringComparison.OrdinalIgnoreCase));
    }
}
