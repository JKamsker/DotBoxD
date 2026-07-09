using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class PolicyGrantMutationTests
{
    [Fact]
    public async Task Event_grants_reject_parameters_and_unsupported_event_ids()
    {
        var eventWithParameter = new SandboxPolicy(
            "event-with-parameter",
            SandboxEffects.Pure,
            [new CapabilityGrant("event.read.health", new Dictionary<string, string> { ["scope"] = "combat" })],
            new ResourceLimits(MaxFuel: 1_000));
        var unsupportedEvent = new SandboxPolicy(
            "unsupported-event",
            SandboxEffects.Pure,
            [new CapabilityGrant("event.read.secret", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

        var parameterEx = await PolicyMutationTestSupport.PrepareThrowsAsync(
            PolicyMutationTestSupport.EventRequestModule("event.read.health"),
            eventWithParameter);
        var unsupportedEx = await PolicyMutationTestSupport.PrepareThrowsAsync(
            PolicyMutationTestSupport.EventRequestModule("event.read.health"),
            unsupportedEvent);

        PolicyMutationTestSupport.AssertDiagnostic(
            parameterEx,
            "E-POLICY-GRANT-PARAM",
            "parameter 'scope' is not supported");
        Assert.Single(parameterEx.Diagnostics, d =>
            d.Code == "E-POLICY-GRANT-PARAM" &&
            d.Message.Contains("parameter 'scope' is not supported", StringComparison.Ordinal));
        PolicyMutationTestSupport.AssertDiagnostic(
            unsupportedEx,
            "E-POLICY-GRANT",
            "grant 'event.read.secret' is not supported");
    }

    [Fact]
    public async Task Direct_event_grant_supports_matching_requested_event_capability()
    {
        var policy = new SandboxPolicy(
            "event-direct",
            SandboxEffects.Pure,
            [new CapabilityGrant("event.read.health", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

        var plan = await PolicyMutationTestSupport.CreateDefaultHost().PrepareAsync(
            PolicyMutationTestSupport.EventRequestModule("event.read.health"),
            policy);

        Assert.Contains(plan.Policy.Grants, grant => grant.Id == "event.read.health");
    }

    [Fact]
    public async Task Unknown_non_event_grant_reports_the_grant_id()
    {
        var policy = new SandboxPolicy(
            "unsupported-vendor-grant",
            SandboxEffects.Pure,
            [new CapabilityGrant("vendor.secret", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            policy);

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT",
            "grant 'vendor.secret' is not supported by the prepared module");
    }

    [Fact]
    public async Task Wildcard_event_grant_supports_matching_requested_event_capability()
    {
        var policy = new SandboxPolicy(
            "event-wildcard",
            SandboxEffects.Pure,
            [new CapabilityGrant("event.read.*", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

        var plan = await PolicyMutationTestSupport.CreateDefaultHost().PrepareAsync(
            PolicyMutationTestSupport.EventRequestModule("event.read.health"),
            policy);

        Assert.Contains(plan.Policy.Grants, grant => grant.Id == "event.read.*");
    }

    [Fact]
    public async Task Wildcard_event_grant_rejects_parameters_for_matching_event_capability()
    {
        var policy = new SandboxPolicy(
            "event-wildcard-parameters",
            SandboxEffects.Pure,
            [new CapabilityGrant("event.read.*", new Dictionary<string, string> { ["scope"] = "combat" })],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(
            PolicyMutationTestSupport.EventRequestModule("event.read.health"),
            policy);

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT-PARAM",
            "parameter 'scope' is not supported");
    }

    [Fact]
    public async Task Wildcard_grants_reject_unmatched_and_unsupported_requested_capabilities()
    {
        var unmatched = new SandboxPolicy(
            "unmatched-wildcard",
            SandboxEffects.Pure,
            [new CapabilityGrant("event.read.*", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));
        var unsupported = new SandboxPolicy(
            "unsupported-request-wildcard",
            SandboxEffects.Pure,
            [new CapabilityGrant("vendor.*", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

        var unmatchedEx = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            unmatched);
        var unsupportedEx = await PolicyMutationTestSupport.PrepareThrowsAsync(
            PolicyMutationTestSupport.EventRequestModule("vendor.secret"),
            unsupported);

        PolicyMutationTestSupport.AssertDiagnostic(
            unmatchedEx,
            "E-POLICY-GRANT",
            "grant 'event.read.*' is not supported");
        PolicyMutationTestSupport.AssertDiagnostic(
            unsupportedEx,
            "E-POLICY-GRANT",
            "wildcard grant 'vendor.*' matches requested capability 'vendor.secret'");
    }

    [Fact]
    public async Task Reentrant_grant_is_explicitly_rejected_until_supported()
    {
        var policy = new SandboxPolicy(
            "reentrant-grant",
            SandboxEffects.Pure | SandboxEffect.Concurrency,
            [new CapabilityGrant(RuntimeCapabilityIds.Reentrant, new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            policy);

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT",
            $"grant '{RuntimeCapabilityIds.Reentrant}' is not supported");
    }

    [Fact]
    public async Task Built_in_runtime_grants_reject_parameters()
    {
        var policy = new SandboxPolicy(
            "log-parameter",
            SandboxEffects.Pure | SandboxEffect.Audit,
            [new CapabilityGrant("log.write", new Dictionary<string, string> { ["scope"] = "debug" })],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            policy);

        PolicyMutationTestSupport.AssertDiagnostic(
            ex,
            "E-POLICY-GRANT-PARAM",
            "grant 'log.write' parameter 'scope' is not supported");
    }

    [Fact]
    public async Task Unused_valid_file_read_grant_is_supported_by_the_host()
    {
        using var temp = PolicyMutationTestSupport.TempDirectory.Create();
        var policy = new SandboxPolicy(
            "unused-file-read",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [new CapabilityGrant("file.read", PolicyMutationTestSupport.FileReadParameters(temp.Path))],
            new ResourceLimits(MaxFuel: 1_000, MaxFileBytesRead: 1024));

        var plan = await PolicyMutationTestSupport.CreateDefaultHost().PrepareAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            policy);

        Assert.Contains(plan.Policy.Grants, grant => grant.Id == "file.read");
    }

    [Fact]
    public async Task Expiring_file_grant_is_not_validated_at_the_expiry_boundary()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new SandboxPolicy(
            "expired-at-boundary",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [
                new CapabilityGrant(
                    "file.read",
                    PolicyMutationTestSupport.FileReadParameters("relative/config"),
                    ExpiresAt: now)
            ],
            new ResourceLimits(MaxFuel: 1_000, MaxFileBytesRead: 1024),
            Deterministic: true,
            LogicalNow: now,
            RandomSeed: 1);

        var ex = await PolicyMutationTestSupport.PrepareThrowsAsync(
            await PolicyMutationTestSupport.FileReadModuleAsync(),
            policy);

        PolicyMutationTestSupport.AssertDiagnostic(ex, "E-POLICY-CAP", "file.read");
        Assert.DoesNotContain(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    [Fact]
    public async Task Registered_custom_grants_are_supported_and_run_custom_validators()
    {
        var host = PolicyMutationTestSupport.CustomCapabilityHost();
        var unusedRegisteredPolicy = new SandboxPolicy(
            "unused-custom-grant",
            SandboxEffects.Pure | SandboxEffect.HostStateRead | SandboxEffect.Audit,
            [new CapabilityGrant("probe.read", new Dictionary<string, string> { ["scope"] = "inventory" })],
            new ResourceLimits(MaxFuel: 1_000));
        var malformedPolicy = new SandboxPolicy(
            "malformed-custom-grant",
            SandboxEffects.Pure | SandboxEffect.HostStateRead | SandboxEffect.Audit,
            [new CapabilityGrant("probe.read", new Dictionary<string, string> { ["scope"] = "" })],
            new ResourceLimits(MaxFuel: 1_000));

        var plan = await host.PrepareAsync(
            await PolicyMutationTestSupport.PureModuleAsync(),
            unusedRegisteredPolicy);
        var malformedEx = await PolicyMutationTestSupport.PrepareThrowsAsync(
            host,
            await PolicyMutationTestSupport.CustomBindingModuleAsync(),
            malformedPolicy);

        Assert.Contains(plan.Policy.Grants, grant => grant.Id == "probe.read");
        PolicyMutationTestSupport.AssertDiagnostic(malformedEx, "E-PROBE-GRANT", "scope is required");
    }
}
