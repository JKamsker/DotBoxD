using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy.Surprise;

public sealed class DeclaredFunctionEffectsPolicyTests
{
    [Theory]
    [MemberData(nameof(DisallowedDeclaredEffects))]
    public async Task Prepare_rejects_entrypoint_declared_effects_not_allowed_by_policy(
        SandboxEffect declaredEffects,
        string expectedMessageFragment)
    {
        var host = SandboxTestHost.Create();
        var module = ModuleWithDeclaredEffects(declaredEffects);

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(exception.Diagnostics, diagnostic =>
            diagnostic.Code == "E-POLICY-EFFECT" &&
            diagnostic.Message.Contains("declared", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains(expectedMessageFragment, StringComparison.OrdinalIgnoreCase));
    }

    public static TheoryData<SandboxEffect, string> DisallowedDeclaredEffects()
        => new()
        {
            { SandboxEffect.FileWrite, nameof(SandboxEffect.FileWrite) },
            { (SandboxEffect)(1 << 20), "unknown" }
        };

    private static SandboxModule ModuleWithDeclaredEffects(SandboxEffect declaredEffects)
        => new(
            "declared-effects-policy",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [
                        new ReturnStatement(
                            new LiteralExpression(SandboxValue.Unit, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ],
                    DeclaredEffects: declaredEffects)
            ],
            new Dictionary<string, string>());
}
