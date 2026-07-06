using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

internal static class WorkerRunSummaryAuditValidator
{
    private static readonly HashSet<string> CommonRunSummaryFields = [
        "mode",
        "executionMode",
        "executionDispatched",
        "cacheStatus",
        "moduleHash",
        "planHash",
        "policyId",
        "policyHash",
        "bindingManifestHash",
        "fuelUsed",
        "maxFuel",
        "loopIterations",
        "maxLoopIterations",
        "allocatedBytes",
        "allocationCharged",
        "maxAllocatedBytes",
        "hostCalls",
        "maxHostCalls",
        "fileBytesRead",
        "maxFileBytesRead",
        "fileBytesWritten",
        "maxFileBytesWritten",
        "networkBytesRead",
        "maxNetworkBytesRead",
        "networkBytesWritten",
        "maxNetworkBytesWritten",
        "logEvents",
        "maxLogEvents",
        "collectionElements",
        "maxCollectionElements",
        "stringBytes",
        "maxStringBytes"
    ];

    public static bool Matches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (!EnvelopeMatches(plan, auditEvent))
        {
            return false;
        }

        foreach (var field in auditEvent.Fields!)
        {
            if (!FieldMatches(plan, field))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EnvelopeMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.Fields is not null &&
           string.Equals(auditEvent.ResourceId, $"module:{plan.ModuleHash}", StringComparison.Ordinal);

    private static bool FieldMatches(ExecutionPlan plan, KeyValuePair<string, string> field)
        => FieldNameAllowed(plan, field.Key) &&
           !string.IsNullOrWhiteSpace(field.Key) &&
           WorkerAuditTextSafety.TextIsSafe(field.Key) &&
           WorkerAuditTextSafety.TextIsSafe(field.Value);

    private static bool FieldNameAllowed(ExecutionPlan plan, string key)
        => CommonRunSummaryFields.Contains(key) ||
           (plan.Policy.Deterministic && key == "logicalNow") ||
           key is "runtimeForm" or "cacheKey" or "artifactHash" or "materializationStatus";
}
