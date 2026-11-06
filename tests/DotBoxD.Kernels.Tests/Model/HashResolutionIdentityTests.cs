using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Model;

// Canonical hashes key compiled-artifact caches and audit records, so two inputs that validate
// or resolve differently must never share a hash.
public sealed class HashResolutionIdentityTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void Module_hash_distinguishes_declared_function_effects()
    {
        var undeclared = ModuleWithEffects(declaredEffects: null);
        var pureOnly = ModuleWithEffects(SandboxEffect.Cpu);

        Assert.NotEqual(CanonicalModuleHasher.Hash(undeclared), CanonicalModuleHasher.Hash(pureOnly));
        Assert.NotEqual(
            CanonicalModuleHasher.Hash(ModuleWithEffects(SandboxEffect.Cpu)),
            CanonicalModuleHasher.Hash(ModuleWithEffects(SandboxEffect.Cpu | SandboxEffect.Alloc)));
        Assert.Equal(
            CanonicalModuleHasher.Hash(ModuleWithEffects(SandboxEffect.Cpu)),
            CanonicalModuleHasher.Hash(ModuleWithEffects(SandboxEffect.Cpu)));
    }

    [Fact]
    public void Module_hash_differs_when_declared_effects_change_the_validation_outcome()
    {
        var validator = new ModuleValidator();
        var bindings = new BindingRegistry([]);
        var policy = Policy();
        var accepted = ModuleWithEffects(declaredEffects: null);
        var rejected = ModuleWithEffects(SandboxEffect.FileWrite);

        Assert.True(validator.Validate(accepted, bindings, policy).Succeeded);
        Assert.False(validator.Validate(rejected, bindings, policy).Succeeded);
        Assert.NotEqual(CanonicalModuleHasher.Hash(accepted), CanonicalModuleHasher.Hash(rejected));
    }

    [Fact]
    public void Policy_hash_distinguishes_wildcard_grant_order_that_changes_resolution()
    {
        var broad = new CapabilityGrant("game.*", new Dictionary<string, string> { ["scope"] = "broad" });
        var narrow = new CapabilityGrant("game.world.*", new Dictionary<string, string> { ["scope"] = "narrow" });
        var broadFirst = Policy(broad, narrow);
        var narrowFirst = Policy(narrow, broad);

        Assert.NotEqual(
            broadFirst.GetGrant("game.world.health").Parameters["scope"],
            narrowFirst.GetGrant("game.world.health").Parameters["scope"]);
        Assert.NotEqual(broadFirst.Hash, narrowFirst.Hash);
    }

    [Fact]
    public void Policy_hash_stays_independent_of_exact_grant_order()
    {
        var read = new CapabilityGrant("file.read", new Dictionary<string, string>());
        var log = new CapabilityGrant("log.write", new Dictionary<string, string>());
        var wildcard = new CapabilityGrant("game.*", new Dictionary<string, string>());

        Assert.Equal(Policy(read, log, wildcard).Hash, Policy(log, wildcard, read).Hash);
    }

    private static SandboxPolicy Policy(params CapabilityGrant[] grants)
        => new("hash-resolution-identity", SandboxEffects.Pure, grants, new ResourceLimits(MaxFuel: 1_000));

    private static SandboxModule ModuleWithEffects(SandboxEffect? declaredEffects)
        => new(
            "declared-effects-hash",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.I32,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(1), Span), Span)],
                    declaredEffects)
            ],
            new Dictionary<string, string>());
}
