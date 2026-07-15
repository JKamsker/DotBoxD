using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

internal static class InterpreterCancellationBoundary
{
    private const string RunSummaryKind = "RunSummary";

    public static SandboxExecutionResult CancelledResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxExecutionResult completedResult)
    {
        var audit = new InMemoryAuditSink();
        SandboxAuditEvent? returnedSummary = null;
        SandboxRunId? returnedRunId = null;
        foreach (var auditEvent in completedResult.AuditEvents)
        {
            returnedRunId ??= auditEvent.RunId;
            if (StringComparer.Ordinal.Equals(auditEvent.Kind, RunSummaryKind))
            {
                returnedSummary = auditEvent;
                continue;
            }

            audit.Write(auditEvent);
        }

        var runId = options.RunId ?? returnedSummary?.RunId ?? returnedRunId ?? SandboxRunId.New();
        var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
        audit.Write(CancelledRunSummary(
            plan,
            completedResult.ResourceUsage,
            runId,
            returnedSummary?.Timestamp ?? AuditTime(plan),
            error));
        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = completedResult.ResourceUsage,
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxAuditEvent CancelledRunSummary(
        ExecutionPlan plan,
        SandboxResourceUsage usage,
        SandboxRunId runId,
        DateTimeOffset timestamp,
        SandboxError error)
    {
        var fields = RunSummaryAuditFields.Create(
            plan,
            usage,
            plan.Budget,
            ExecutionMode.Interpreted,
            "None");
        return new SandboxAuditEvent(
            runId,
            RunSummaryKind,
            timestamp,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: $"mode=interpreted cacheStatus=None plan={plan.PlanHash} " +
                     $"policy={plan.PolicyHash} policyId={fields["policyId"]} " +
                     $"bindings={plan.BindingManifestHash} " +
                     $"fuel={usage.FuelUsed}/{plan.Budget.MaxFuel}",
            Fields: fields);
    }

    private static DateTimeOffset AuditTime(ExecutionPlan plan)
        => plan.Policy.Deterministic
            ? plan.Policy.LogicalNow ?? DateTimeOffset.UnixEpoch
            : DateTimeOffset.UtcNow;
}
