using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Execution;

using DotBoxD.Kernels;

internal static class InterpreterResultAuditValidator
{
    private const string RunSummaryKind = "RunSummary";

    public static bool TrySanitize(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        out SandboxExecutionResult validationResult,
        out IReadOnlyList<SandboxAuditEvent> sanitizedAuditEvents)
    {
        validationResult = null!;
        sanitizedAuditEvents = [];
        if (!TryGetCanonicalSummary(plan, entrypoint, options, result, out var summary))
        {
            return false;
        }

        var sanitizer = new InterpreterAuditEvidenceSanitizer(
            plan,
            entrypoint,
            options,
            result,
            summary);
        return sanitizer.TrySanitize(out validationResult, out sanitizedAuditEvents);
    }

    private static bool TryGetCanonicalSummary(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        out SandboxAuditEvent summary)
    {
        var summaries = result.AuditEvents
            .Where(auditEvent =>
                auditEvent.Kind == RunSummaryKind &&
                WorkerAuditValidator.Matches(
                    plan,
                    entrypoint,
                    options,
                    auditEvent,
                    plan.Policy.GrantClock))
            .ToArray();
        if (summaries.Length != 1)
        {
            summary = null!;
            return false;
        }

        summary = summaries[0];
        return options.RunId is null || summary.RunId == options.RunId;
    }
}
