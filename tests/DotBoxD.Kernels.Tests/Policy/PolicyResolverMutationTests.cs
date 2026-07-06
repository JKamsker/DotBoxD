using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class PolicyResolverMutationTests
{
    [Fact]
    public async Task Policy_diagnostics_include_missing_requested_and_required_capability_ids()
    {
        var requestedEx = await PolicyMutationTestSupport.PrepareThrowsAsync(
            PolicyMutationTestSupport.EventRequestModule("event.read.health"),
            PolicyMutationTestSupport.PurePolicy("missing-requested-event"));
        var requiredEx = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.FileReadModuleAsync(),
            PolicyMutationTestSupport.PurePolicy("missing-file-read"));

        PolicyMutationTestSupport.AssertDiagnostic(
            requestedEx,
            "E-POLICY-CAP",
            "requested capability 'event.read.health' is not granted");
        PolicyMutationTestSupport.AssertDiagnostic(
            requiredEx,
            "E-POLICY-CAP",
            "required capability 'file.read' is not granted");
    }

    [Fact]
    public async Task Policy_diagnostics_include_denied_effect_names()
    {
        var policy = new SandboxPolicy(
            "denied-time-effect",
            SandboxEffects.Pure,
            [new CapabilityGrant("time.now", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.TimeModuleAsync(),
            policy);

        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-EFFECT", "policy denies declared effects Time");
    }

    [Theory]
    [InlineData(SandboxEffect.Time, "time.now", "deterministic policy requires logical time for Time effects")]
    [InlineData(SandboxEffect.Random, "random", "deterministic policy requires a random seed for Random effects")]
    public async Task Deterministic_policies_require_sources_for_time_and_random_effects(
        SandboxEffect effect,
        string capability,
        string expectedMessage)
    {
        var module = effect == SandboxEffect.Time
            ? await PolicyMutationTestSupport.TimeModuleAsync()
            : await PolicyMutationTestSupport.RandomModuleAsync();
        var policy = new SandboxPolicy(
            "deterministic-missing-source",
            SandboxEffects.Pure | effect,
            [new CapabilityGrant(capability, new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000),
            Deterministic: true,
            LogicalNow: effect == SandboxEffect.Time ? null : DateTimeOffset.UnixEpoch,
            RandomSeed: effect == SandboxEffect.Random ? null : 1);

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(module, policy);

        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-DETERMINISM", expectedMessage);
    }

    [Fact]
    public async Task Deterministic_policies_reject_async_grants_and_external_effects()
    {
        var allExternal = SandboxEffect.FileRead |
            SandboxEffect.FileWrite |
            SandboxEffect.Network |
            SandboxEffect.HostStateRead |
            SandboxEffect.HostStateWrite;
        var policy = new SandboxPolicy(
            "deterministic-external",
            SandboxEffects.Pure | SandboxEffect.Concurrency | allExternal,
            [new CapabilityGrant(RuntimeCapabilityIds.Async, new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000),
            Deterministic: true,
            LogicalNow: DateTimeOffset.UnixEpoch,
            RandomSeed: 1);

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            policy);

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-DETERMINISM",
            "deterministic policy cannot grant runtime async");
        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-DETERMINISM",
            "deterministic policy denies external effects");
        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-DETERMINISM", "FileRead");
        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-DETERMINISM", "FileWrite");
        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-DETERMINISM", "Network");
        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-DETERMINISM", "HostStateRead");
        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-DETERMINISM", "HostStateWrite");
    }

    [Fact]
    public async Task Deterministic_pure_policies_do_not_require_time_or_random_sources()
    {
        var policy = new SandboxPolicy(
            "deterministic-pure",
            SandboxEffects.Pure,
            [],
            new ResourceLimits(MaxFuel: 1_000),
            Deterministic: true,
            LogicalNow: null,
            RandomSeed: null);

        var plan = await PolicyMutationTestSupport.CreateDefaultHost().PrepareAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            policy);

        Assert.True(plan.Policy.Deterministic);
    }

    [Fact]
    public async Task Unknown_policy_effect_bits_are_rejected_with_named_diagnostic()
    {
        var policy = new SandboxPolicy(
            "unknown-effect-bits",
            SandboxEffects.Pure | (SandboxEffect)(1 << 20),
            [],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            policy);

        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-EFFECT", "policy declares unknown effects");
    }
}
