using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class PolicyGrantValidationRegressionTests
{
    [Fact]
    public async Task Prepare_rejects_null_capability_grant_entries_with_policy_diagnostic()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(LogModuleJson());
        var policy = new SandboxPolicy(
            "null-grant-entry",
            SandboxEffects.Pure | SandboxEffect.Audit,
            [
                null!,
                new CapabilityGrant("log.write", new Dictionary<string, string>())
            ],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "E-POLICY-GRANT" &&
            d.Message.Contains("null", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("grant", StringComparison.OrdinalIgnoreCase));
    }

    private static string LogModuleJson()
        => """
        {
          "id": "null-grant-entry-policy",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "log.info",
                    "args": [{ "string": "hello" }]
                  }
                }
              ]
            }
          ]
        }
        """;
}
