using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class SandboxPolicyOpaqueIdDeclarationTests
{
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
            "String"
        };

    private static bool IsOpaqueIdPolicyDiagnostic(SandboxDiagnostic diagnostic)
        => diagnostic.Code == "E-POLICY-OPAQUE-ID" &&
           diagnostic.Message.Contains("opaque", StringComparison.OrdinalIgnoreCase);

    private static string Display(string? value)
        => value is null ? "<null>" : value.Length == 0 ? "<empty>" : value;
}
