using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Hosting;

internal static class WorkerCacheInvalidationAuditValidator
{
    public static bool Matches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent)
    {
        if (!TryReadFields(plan, entrypoint, auditEvent, out var fields))
        {
            return false;
        }

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) ||
                !TextIsSafe(field.Key) ||
                !TextIsSafe(field.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadFields(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent,
        out IReadOnlyDictionary<string, string> fields)
    {
        fields = null!;
        if (!EnvelopeMatches(auditEvent) ||
            auditEvent.Fields is not { Count: 4 } values ||
            !TryReadRequiredFields(values, out var cacheKey, out var moduleHash, out var planHash, out var reason) ||
            !CacheIdentityMatches(plan, entrypoint, auditEvent, cacheKey, moduleHash, planHash, reason))
        {
            return false;
        }

        fields = values;
        return true;
    }

    private static bool EnvelopeMatches(SandboxAuditEvent auditEvent)
        => !auditEvent.Success &&
           auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.ErrorCode == SandboxErrorCode.CacheInvalid;

    private static bool TryReadRequiredFields(
        IReadOnlyDictionary<string, string> values,
        out string cacheKey,
        out string moduleHash,
        out string planHash,
        out string reason)
    {
        cacheKey = string.Empty;
        moduleHash = string.Empty;
        planHash = string.Empty;
        reason = string.Empty;
        return values.TryGetValue("cacheKey", out cacheKey!) &&
            values.TryGetValue("moduleHash", out moduleHash!) &&
            values.TryGetValue("planHash", out planHash!) &&
            values.TryGetValue("reason", out reason!);
    }

    private static bool CacheIdentityMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent,
        string cacheKey,
        string moduleHash,
        string planHash,
        string reason)
        => ExpectedCacheKeyMatches(plan, entrypoint, cacheKey) &&
           string.Equals(auditEvent.ResourceId, "cache:" + cacheKey, StringComparison.Ordinal) &&
           string.Equals(moduleHash, plan.ModuleHash, StringComparison.Ordinal) &&
           string.Equals(planHash, plan.PlanHash, StringComparison.Ordinal) &&
           !string.IsNullOrWhiteSpace(reason);

    private static bool ExpectedCacheKeyMatches(ExecutionPlan plan, string entrypoint, string cacheKey)
    {
        var policy = VerificationPolicy.BoxedValueDefaults();
        return string.Equals(cacheKey, CacheKeyBuilder.Build(plan, entrypoint, policy, optimize: false), StringComparison.Ordinal) ||
               string.Equals(cacheKey, CacheKeyBuilder.Build(plan, entrypoint, policy, optimize: true), StringComparison.Ordinal);
    }

    private static bool TextIsSafe(string? value)
        => value is null ||
           string.Equals(AuditTextSanitizer.SanitizeAndRedact(value), value, StringComparison.Ordinal);
}
