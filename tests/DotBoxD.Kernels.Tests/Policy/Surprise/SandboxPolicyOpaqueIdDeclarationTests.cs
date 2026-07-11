using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class SandboxPolicyOpaqueIdDeclarationTests
{
    [Fact]
    public async Task Default_policy_opaque_id_declarations_are_not_mutable_shared_authority()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleReturningOpaqueId());
        var exposedPolicy = DefaultPolicy("exposed-default-policy");
        var targetPolicy = DefaultPolicy("target-default-policy");

        var before = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, targetPolicy));

        Assert.Contains(before.Diagnostics, IsOpaqueIdPolicyDiagnostic);

        if (exposedPolicy.DeclaredOpaqueIdTypes is ISet<string> mutableDeclarations)
        {
            mutableDeclarations.Add("PlayerId");
        }

        var after = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, targetPolicy));

        Assert.Contains(after.Diagnostics, IsOpaqueIdPolicyDiagnostic);
        Assert.DoesNotContain("PlayerId", targetPolicy.DeclaredOpaqueIdTypes);
    }

    [Theory]
    [MemberData(nameof(InvalidOpaqueIdDeclarations))]
    public async Task Prepare_rejects_direct_policy_with_invalid_declared_opaque_id_types(string? invalidName)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());

        var exception = await Record.ExceptionAsync(async () =>
        {
            var policy = new SandboxPolicy(
                "invalid-opaque-id-declaration",
                SandboxEffects.Pure,
                [],
                new ResourceLimits(MaxFuel: 5_000),
                DeclaredOpaqueIdTypes: new HashSet<string>(StringComparer.Ordinal) { invalidName! });

            await host.PrepareAsync(module, policy);
        });

        if (exception is null)
        {
            Assert.Fail($"PrepareAsync accepted invalid declared opaque-id type '{Display(invalidName)}'.");
        }

        switch (exception)
        {
            case SandboxValidationException validation:
                Assert.Contains(validation.Diagnostics, IsOpaqueIdPolicyDiagnostic);
                break;
            case ArgumentException argument:
                Assert.Contains("opaque", argument.Message, StringComparison.OrdinalIgnoreCase);
                break;
            default:
                Assert.Fail($"Expected a policy validation failure for '{Display(invalidName)}', but got {exception.GetType().Name}.");
                break;
        }
    }

    public static TheoryData<string?> InvalidOpaqueIdDeclarations()
        => new()
        {
            null,
            string.Empty,
            "Unit",
            "Bool",
            "I32",
            "I64",
            "F64",
            "String",
            "Guid",
            "SandboxPath",
            "SandboxUri"
        };

    private static bool IsOpaqueIdPolicyDiagnostic(SandboxDiagnostic diagnostic)
        => diagnostic.Code == "E-TYPE-UNKNOWN" ||
           (diagnostic.Code == "E-POLICY-OPAQUE-ID" &&
            diagnostic.Message.Contains("opaque", StringComparison.OrdinalIgnoreCase));

    private static string Display(string? value)
        => value is null ? "<null>" : value.Length == 0 ? "<empty>" : value;

    private static SandboxPolicy DefaultPolicy(string policyId)
        => new(
            policyId,
            SandboxEffects.Pure,
            [],
            new ResourceLimits(MaxFuel: 5_000));

    private static string ModuleReturningOpaqueId()
        => """
        {
          "id": "opaque-id-policy-mutation",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "PlayerId",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "opaqueId": {
                      "type": "PlayerId",
                      "value": "player-1"
                    }
                  }
                }
              ]
            }
          ]
        }
        """;
}
