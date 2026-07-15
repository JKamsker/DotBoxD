using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Tests.Hosting;

using static InterpreterSecurityValidationTestSupport;

public sealed class InterpreterBindingAuditSecurityValidationTests
{
    private const string BindingId = "test.audited.security";
    private const string ForgedCapability = "authorization bearer secret token";

    [Theory]
    [InlineData(false, "Infinity", false)]
    [InlineData(true, "0.001", false)]
    [InlineData(false, "0.001", true)]
    [InlineData(true, "0", true)]
    public async Task Binding_duration_must_match_runtime_security_contract(
        bool deterministic,
        string durationMs,
        bool shouldSucceed)
    {
        var binding = AuditedBinding(BindingId, durationMs: durationMs);

        var outcome = await ExecuteAsync(
            SingleBindingModule(BindingId),
            Policy(deterministic),
            configure: builder => builder.AddBinding(binding));

        if (shouldSucceed)
        {
            Assert.True(outcome.Result.Succeeded, outcome.Result.Error?.SafeMessage);
            Assert.Contains(outcome.Observed, auditEvent => auditEvent.BindingId == BindingId);
            return;
        }

        AssertRejectedWithoutPublication(outcome);
    }

    [Fact]
    public async Task Summary_level_binding_audit_is_valid_runtime_evidence()
    {
        var binding = AuditedBinding(BindingId, auditLevel: AuditLevel.Summary);

        var outcome = await ExecuteAsync(
            SingleBindingModule(BindingId),
            Policy(),
            configure: builder => builder.AddBinding(binding));

        Assert.True(outcome.Result.Succeeded, outcome.Result.Error?.SafeMessage);
        Assert.Contains(
            outcome.Observed,
            auditEvent => auditEvent.BindingId == BindingId && auditEvent.Message == AuditMarker);
    }

    [Fact]
    public async Task Capability_less_binding_rejects_forged_capability_without_observer_leak()
    {
        var binding = AuditedBinding(
            BindingId,
            reportedCapability: ForgedCapability);

        var outcome = await ExecuteAsync(
            SingleBindingModule(BindingId),
            Policy(),
            configure: builder => builder.AddBinding(binding));

        AssertRejectedWithoutPublication(outcome, ForgedCapability);
    }
}
