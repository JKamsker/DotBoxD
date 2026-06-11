using SafeIR;
using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class JsonImporterTests
{
    [Fact]
    public void Canonical_hash_is_stable_across_json_property_order()
    {
        var first = SafeIrJsonImporter.Import(SandboxTestHost.PureScoreJson());
        var second = SafeIrJsonImporter.Import("""
        {
          "functions": [
            {
              "body": [
                { "value": { "right": { "i32": 10 }, "left": { "var": "level" }, "op": "mul" }, "name": "base", "op": "set" },
                { "name": "bonus", "op": "set", "value": { "left": { "var": "rarity" }, "op": "mul", "right": { "i32": 25 } } },
                { "op": "return", "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "bonus" } } }
              ],
              "returnType": "I32",
              "parameters": [
                { "type": "I32", "name": "level" },
                { "type": "I32", "name": "rarity" }
              ],
              "visibility": "entrypoint",
              "id": "main"
            }
          ],
          "capabilityRequests": [],
          "targetSandboxVersion": "1.0.0",
          "version": "1.0.0",
          "id": "loot-score"
        }
        """);

        Assert.Equal(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
    }

    [Fact]
    public void Capability_requests_reject_untrusted_grant_parameters()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "bad",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.read", "root": "C:\\" }
          ],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Fact]
    public async Task Forbidden_clr_call_is_rejected_before_execution()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync("""
        {
          "id": "bad",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "System.IO.File.ReadAllText",
                    "args": [{ "string": "secret.txt" }]
                  }
                }
              ]
            }
          ]
        }
        """);

        var policy = SandboxPolicyBuilder.Create().Build();
        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await host.PrepareAsync(module, policy));
        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }
}
