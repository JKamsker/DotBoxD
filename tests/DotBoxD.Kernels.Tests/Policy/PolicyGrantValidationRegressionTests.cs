using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class PolicyGrantValidationRegressionTests
{
    [Fact]
    public async Task Prepare_rejects_null_capability_grant_ids_with_policy_diagnostic()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "null-grant-id-policy",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "hello" }] } }
              ]
            }
          ]
        }
        """);
        var policy = new SandboxPolicy(
            "null-grant-id",
            SandboxEffects.Pure | SandboxEffect.Audit,
            [
                new CapabilityGrant(null!, new Dictionary<string, string>()),
                new CapabilityGrant("log.write", new Dictionary<string, string>())
            ],
            new ResourceLimits(MaxFuel: 5_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(
            ex.Diagnostics,
            diagnostic =>
                diagnostic.Code == "E-POLICY-GRANT" &&
                diagnostic.Message.Contains("grant", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.Message.Contains("id", StringComparison.OrdinalIgnoreCase));
    }
}
