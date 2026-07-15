using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Hosting;

using static InterpreterSecurityValidationTestSupport;

public sealed class InterpreterQuotaAuditEvidenceValidationTests
{
    private const string BindingId = "test.audited.quota";

    [Fact]
    public async Task Terminal_per_binding_call_limit_attempt_preserves_quota_failure()
    {
        var binding = AuditedBinding(BindingId, maxCallsPerRun: 1);

        var outcome = await ExecuteAsync(
            DoubleBindingModule(BindingId),
            Policy(),
            configure: builder => builder.AddBinding(binding));

        AssertQuotaFailure(outcome, expectedHostCalls: 2);
        var bindingEvents = outcome.Result.AuditEvents.Where(e => e.BindingId == BindingId).ToArray();
        Assert.Equal(2, bindingEvents.Length);
        Assert.True(bindingEvents[0].Success);
        Assert.False(bindingEvents[1].Success);
    }

    [Fact]
    public async Task Success_audit_followed_by_return_meter_failure_counts_as_one_call()
    {
        var binding = AuditedBinding(
            BindingId,
            maxCallsPerRun: 1,
            returnType: SandboxType.String,
            returnValue: SandboxValue.FromString("over budget"));
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxHostCalls(10)
            .WithMaxTotalStringBytes(1)
            .Build();

        var outcome = await ExecuteAsync(
            SingleBindingModule(BindingId, "String"),
            policy,
            configure: builder => builder.AddBinding(binding));

        AssertQuotaFailure(outcome, expectedHostCalls: 1);
        var bindingEvents = outcome.Result.AuditEvents.Where(e => e.BindingId == BindingId).ToArray();
        Assert.Equal(2, bindingEvents.Length);
        Assert.True(bindingEvents[0].Success);
        Assert.False(bindingEvents[1].Success);
    }

    [Fact]
    public async Task Duplicate_success_binding_evidence_remains_rejected()
    {
        var binding = AuditedBinding(BindingId, maxCallsPerRun: 1);
        var interpreter = new TransformingInterpreter((_, result) => DuplicateBindingEvent(result, success: true));

        var outcome = await ExecuteAsync(
            SingleBindingModule(BindingId),
            Policy(),
            interpreter,
            builder => builder.AddBinding(binding));

        AssertRejectedWithoutPublication(outcome);
    }

    [Fact]
    public async Task Multiple_failed_call_limit_events_remain_rejected()
    {
        var binding = AuditedBinding(BindingId, maxCallsPerRun: 1);
        var interpreter = new TransformingInterpreter((_, result) => DuplicateBindingEvent(result, success: false));

        var outcome = await ExecuteAsync(
            DoubleBindingModule(BindingId),
            Policy(),
            interpreter,
            builder => builder.AddBinding(binding));

        AssertRejectedWithoutPublication(outcome);
    }

    private static SandboxExecutionResult DuplicateBindingEvent(
        SandboxExecutionResult result,
        bool success)
    {
        var duplicate = Assert.Single(
            result.AuditEvents,
            auditEvent => auditEvent.BindingId == BindingId && auditEvent.Success == success);
        var events = result.AuditEvents.ToList();
        events.Insert(events.FindIndex(auditEvent => auditEvent.Kind == "RunSummary"), duplicate);
        return result with { AuditEvents = Resequence(events) };
    }

    private static void AssertQuotaFailure(
        InterpreterValidationOutcome outcome,
        int expectedHostCalls)
    {
        Assert.False(outcome.Result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, outcome.Result.Error!.Code);
        Assert.Equal(expectedHostCalls, outcome.Result.ResourceUsage.HostCalls);
        Assert.Equal(outcome.Result.AuditEvents, outcome.Observed);
    }
}
