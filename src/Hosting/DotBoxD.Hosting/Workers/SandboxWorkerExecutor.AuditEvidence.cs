using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal sealed partial class SandboxWorkerExecutor
{
    private static bool TryCollectAuditEvidence(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        SandboxRunId runId,
        out WorkerAuditEvidence evidence)
    {
        evidence = new WorkerAuditEvidence(null!, 0, 0, 0, default);
        var summaryCount = 0;
        var observedHostCalls = 0;
        var observedLogEvents = 0;
        var observedBindingBaseFuel = 0L;
        var observedBytes = default(ObservedBindingBytes);
        Dictionary<string, int>? observedBindingCalls = null;
        var expectedSequenceNumber = 1L;
        var grantClock = plan.Policy.GrantClock;
        var deterministicRandom = DeterministicRandomAuditSequence.Create(plan);
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
                if (!TryRecordBindingEvidence(
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
            observedLogEvents += auditEvent.Kind == "SandboxLog" ? 1 : 0;
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
        ref WorkerAuditEvidence evidence)
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
        => kind is "BindingCall" or "SandboxLog" or "PluginMessage";

    private static bool AuditEvidenceUsageMatches(
        SandboxResourceUsage usage,
        int observedHostCalls,
        int observedLogEvents,
        long observedBindingBaseFuel,
        ObservedBindingBytes observedBytes)
        => usage.HostCalls >= observedHostCalls &&
           usage.LogEvents >= observedLogEvents &&
           usage.FuelUsed >= observedBindingBaseFuel &&
           usage.FileBytesRead >= observedBytes.FileBytesRead &&
           usage.FileBytesWritten >= observedBytes.FileBytesWritten &&
           usage.NetworkBytesRead >= observedBytes.NetworkBytesRead &&
           usage.NetworkBytesWritten >= observedBytes.NetworkBytesWritten;

    private readonly record struct WorkerAuditEvidence(
        SandboxAuditEvent Summary,
        int ObservedHostCalls,
        int ObservedLogEvents,
        long ObservedBindingBaseFuel,
        ObservedBindingBytes ObservedBytes);
}
