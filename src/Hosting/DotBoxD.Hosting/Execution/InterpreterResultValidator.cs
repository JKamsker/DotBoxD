using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

using DotBoxD.Kernels;

internal static class InterpreterResultValidator
{
    private const string RunSummaryKind = "RunSummary";

    public static bool IsValid(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result)
    {
        if (!InterpreterEnvelopeMatches(result) ||
            result.AuditEvents.Any(auditEvent => !InterpreterAuditKindIsAllowed(auditEvent.Kind)))
        {
            return false;
        }

        var validationResult = WithValidationSummaryWhenSuppressed(plan, options, result);
        return SandboxWorkerResultValidator.Validate(
            plan,
            entrypoint,
            options,
            validationResult,
            out _);
    }

    private static bool InterpreterEnvelopeMatches(SandboxExecutionResult result)
        => result.ActualMode == ExecutionMode.Interpreted &&
           result.ExecutionDispatched &&
           string.IsNullOrWhiteSpace(result.ArtifactHash);

    private static bool InterpreterAuditKindIsAllowed(string kind)
        => kind is RunSummaryKind or
           "DebugTrace" or
           BindingAuditKinds.BindingCall or
           BindingAuditKinds.SandboxLog or
           BindingAuditKinds.PluginMessage;

    private static SandboxExecutionResult WithValidationSummaryWhenSuppressed(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxExecutionResult result)
    {
        if (!result.Succeeded ||
            !options.SuppressSuccessfulRunSummaryAudit ||
            result.AuditEvents.Any(auditEvent => auditEvent.Kind == RunSummaryKind))
        {
            return result;
        }

        var runId = options.RunId ??
            (result.AuditEvents.Count == 0 ? SandboxRunId.New() : result.AuditEvents[0].RunId);
        var events = new SandboxAuditEvent[result.AuditEvents.Count + 1];
        for (var index = 0; index < result.AuditEvents.Count; index++)
        {
            events[index] = result.AuditEvents[index];
        }

        events[^1] = SuccessfulRunSummary(
            plan,
            result.ResourceUsage,
            runId,
            events.Length);
        return result with { AuditEvents = events };
    }

    private static SandboxAuditEvent SuccessfulRunSummary(
        ExecutionPlan plan,
        SandboxResourceUsage usage,
        SandboxRunId runId,
        long sequenceNumber)
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
            AuditTime(plan),
            true,
            ResourceId: $"module:{plan.ModuleHash}",
            Message: $"mode=interpreted cacheStatus=None plan={plan.PlanHash} " +
                     $"policy={plan.PolicyHash} policyId={fields["policyId"]} " +
                     $"bindings={plan.BindingManifestHash} " +
                     $"fuel={usage.FuelUsed}/{plan.Budget.MaxFuel}",
            Fields: fields,
            SequenceNumber: sequenceNumber);
    }

    private static DateTimeOffset AuditTime(ExecutionPlan plan)
        => plan.Policy.Deterministic
            ? plan.Policy.LogicalNow ?? DateTimeOffset.UnixEpoch
            : DateTimeOffset.UtcNow;
}
