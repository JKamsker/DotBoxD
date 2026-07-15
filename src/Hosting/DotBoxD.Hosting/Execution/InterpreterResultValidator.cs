using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

using DotBoxD.Kernels;

internal static class InterpreterResultValidator
{
    private const string RunSummaryKind = "RunSummary";

    public static bool TryValidate(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        out SandboxExecutionResult validatedResult)
    {
        validatedResult = null!;
        if (!InterpreterEnvelopeMatches(result))
        {
            return false;
        }

        var validationCandidate = WithValidationSummaryWhenSuppressed(
            plan,
            options,
            result,
            out var summaryWasSynthesized);
        if (!InterpreterResultAuditValidator.TrySanitize(
                plan,
                entrypoint,
                options,
                validationCandidate,
                out var workerValidationResult,
                out var sanitizedAuditEvents) ||
            !SandboxWorkerResultValidator.ValidateInterpreterEnvelope(
                plan,
                entrypoint,
                options,
                workerValidationResult,
                out _) ||
            !SandboxWorkerDeterministicBindingValidator.Matches(
                plan,
                entrypoint,
                validationCandidate with { AuditEvents = sanitizedAuditEvents }))
        {
            return false;
        }

        if (summaryWasSynthesized)
        {
            sanitizedAuditEvents = sanitizedAuditEvents
                .Where(auditEvent => auditEvent.Kind != RunSummaryKind)
                .ToSequencedArray();
        }

        validatedResult = result with { AuditEvents = sanitizedAuditEvents };
        return true;
    }

    private static bool InterpreterEnvelopeMatches(SandboxExecutionResult result)
        => result.ActualMode == ExecutionMode.Interpreted &&
           result.ExecutionDispatched &&
           string.IsNullOrWhiteSpace(result.ArtifactHash);

    private static SandboxExecutionResult WithValidationSummaryWhenSuppressed(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        out bool summaryWasSynthesized)
    {
        if (!result.Succeeded ||
            !options.SuppressSuccessfulRunSummaryAudit ||
            result.AuditEvents.Any(auditEvent => auditEvent.Kind == RunSummaryKind))
        {
            summaryWasSynthesized = false;
            return result;
        }

        summaryWasSynthesized = true;
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
