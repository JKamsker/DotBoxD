using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal static class SandboxWorkerAuditEvidenceCollector
{
    public static bool TryCollect(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        SandboxRunId runId,
        out SandboxWorkerAuditEvidence evidence)
    {
        evidence = new SandboxWorkerAuditEvidence(null!, 0, 0, 0, default);
        var summaryCount = 0;
        var observedHostCalls = 0;
        var observedLogEvents = 0;
        var observedBindingBaseFuel = 0L;
        var observedBytes = default(SandboxWorkerBindingEvidence.ObservedBindingBytes);
        Dictionary<string, int>? observedBindingCalls = null;
        var expectedSequenceNumber = 1L;
        var grantClock = plan.Policy.GrantClock;
        var deterministicRandom = SandboxWorkerBindingEvidence.DeterministicRandomAuditSequence.Create(plan);

        foreach (var auditEvent in result.AuditEvents)
        {
            if (!AuditEventEnvelopeMatches(plan, entrypoint, options, auditEvent, runId, expectedSequenceNumber, grantClock))
            {
                return false;
            }

            expectedSequenceNumber++;
            if (IsBindingAudit(auditEvent.Kind))
            {
                if (result.Succeeded && !auditEvent.Success)
                {
                    return false;
                }

                observedHostCalls++;
                observedBindingCalls ??= new Dictionary<string, int>(StringComparer.Ordinal);
                if (!SandboxWorkerBindingEvidence.TryRecordBindingEvidence(
                    plan,
                    auditEvent,
                    observedBindingCalls,
                    ref observedBindingBaseFuel,
                    ref observedBytes,
                    ref deterministicRandom,
                    grantClock))
                {
                    return false;
                }
            }

            CountAuditSummary(auditEvent, ref summaryCount, ref evidence);
            observedLogEvents += auditEvent.Kind == BindingAuditKinds.SandboxLog ? 1 : 0;
        }

        evidence = evidence with
        {
            ObservedHostCalls = observedHostCalls,
            ObservedLogEvents = observedLogEvents,
            ObservedBindingBaseFuel = observedBindingBaseFuel,
            ObservedBytes = observedBytes,
        };
        return summaryCount == 1;
    }

    public static bool UsageMatches(SandboxResourceUsage usage, SandboxWorkerAuditEvidence evidence)
        => usage.HostCalls >= evidence.ObservedHostCalls &&
           usage.LogEvents >= evidence.ObservedLogEvents &&
           usage.FuelUsed >= evidence.ObservedBindingBaseFuel &&
           usage.FileBytesRead >= evidence.ObservedBytes.FileBytesRead &&
           usage.FileBytesWritten >= evidence.ObservedBytes.FileBytesWritten &&
           usage.NetworkBytesRead >= evidence.ObservedBytes.NetworkBytesRead &&
           usage.NetworkBytesWritten >= evidence.ObservedBytes.NetworkBytesWritten;

    private static bool AuditEventEnvelopeMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxAuditEvent auditEvent,
        SandboxRunId runId,
        long expectedSequenceNumber,
        DateTimeOffset grantClock)
        => auditEvent.SequenceNumber == expectedSequenceNumber &&
           auditEvent.RunId == runId &&
           WorkerAuditValidator.Matches(plan, entrypoint, options, auditEvent, grantClock);

    private static void CountAuditSummary(
        SandboxAuditEvent auditEvent,
        ref int summaryCount,
        ref SandboxWorkerAuditEvidence evidence)
    {
        if (auditEvent.Kind != "RunSummary")
        {
            return;
        }

        summaryCount++;
        if (summaryCount == 1)
        {
            evidence = evidence with { Summary = auditEvent };
        }
    }

    private static bool IsBindingAudit(string kind)
        => kind is BindingAuditKinds.BindingCall or BindingAuditKinds.SandboxLog or BindingAuditKinds.PluginMessage;
}

internal readonly record struct SandboxWorkerAuditEvidence(
    SandboxAuditEvent Summary,
    int ObservedHostCalls,
    int ObservedLogEvents,
    long ObservedBindingBaseFuel,
    SandboxWorkerBindingEvidence.ObservedBindingBytes ObservedBytes);
