using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Hosting.Worker;

internal static class WorkerRunSummaryValidator
{
    private static readonly RunSummaryField[] RequiredFields =
    [
        new("mode", (_, result) => result.ActualMode.ToString()),
        new("executionMode", (_, result) => result.ActualMode.ToString()),
        new("executionDispatched", (_, _) => true.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("moduleHash", (plan, _) => plan.ModuleHash),
        new("planHash", (plan, _) => plan.PlanHash),
        new("policyId", (plan, _) => ExpectedPolicyId(plan)),
        new("policyHash", (plan, _) => plan.PolicyHash),
        new("bindingManifestHash", (plan, _) => plan.BindingManifestHash),
        new("fuelUsed", (_, result) => Format(result.ResourceUsage.FuelUsed)),
        new("loopIterations", (_, result) => Format(result.ResourceUsage.LoopIterations)),
        new("allocatedBytes", (_, result) => Format(result.ResourceUsage.AllocatedBytes)),
        new("allocationCharged", (_, result) => Format(result.ResourceUsage.AllocatedBytes)),
        new("hostCalls", (_, result) => Format(result.ResourceUsage.HostCalls)),
        new("fileBytesRead", (_, result) => Format(result.ResourceUsage.FileBytesRead)),
        new("fileBytesWritten", (_, result) => Format(result.ResourceUsage.FileBytesWritten)),
        new("networkBytesRead", (_, result) => Format(result.ResourceUsage.NetworkBytesRead)),
        new("networkBytesWritten", (_, result) => Format(result.ResourceUsage.NetworkBytesWritten)),
        new("logEvents", (_, result) => Format(result.ResourceUsage.LogEvents)),
        new("collectionElements", (_, result) => Format(result.ResourceUsage.CollectionElements)),
        new("stringBytes", (_, result) => Format(result.ResourceUsage.StringBytes)),
    ];

    internal static bool RunSummaryMatches(
        ExecutionPlan plan,
        SandboxExecutionResult result,
        SandboxAuditEvent summary)
    {
        if (summary.Fields is null || !RequiredFieldsMatch(plan, result, summary) || !HasNonEmptyField(summary, "cacheStatus"))
        {
            return false;
        }

        if (!WorkerEnvelopeValidators.BudgetFieldsMatch(plan, summary))
        {
            return false;
        }

        return RuntimeEnvelopeMatches(result, summary);
    }

    private static bool RequiredFieldsMatch(
        ExecutionPlan plan,
        SandboxExecutionResult result,
        SandboxAuditEvent summary)
    {
        foreach (var field in RequiredFields)
        {
            if (!FieldEquals(summary, field.Name, field.Expected(plan, result)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RuntimeEnvelopeMatches(SandboxExecutionResult result, SandboxAuditEvent summary)
    {
        if (result.ActualMode != ExecutionMode.Compiled)
        {
            return CompiledEnvelopeFieldsAbsent(summary);
        }

        return result.Succeeded
            ? SucceededCompiledEnvelopeMatches(result, summary)
            : FailedCompiledEnvelopeMatches(result, summary);
    }

    private static bool SucceededCompiledEnvelopeMatches(SandboxExecutionResult result, SandboxAuditEvent summary)
        => IsHexSha256(result.ArtifactHash) &&
           FieldEquals(summary, "artifactHash", result.ArtifactHash!) &&
           FieldEquals(summary, "runtimeForm", "LoadedAssembly") &&
           HasHexSha256Field(summary, "cacheKey");

    private static bool FailedCompiledEnvelopeMatches(SandboxExecutionResult result, SandboxAuditEvent summary)
    {
        var hasResultArtifact = !string.IsNullOrWhiteSpace(result.ArtifactHash);
        if (!hasResultArtifact)
        {
            return CompiledEnvelopeFieldsAbsent(summary);
        }

        return IsHexSha256(result.ArtifactHash) &&
               FieldEquals(summary, "artifactHash", result.ArtifactHash!) &&
               FieldEquals(summary, "runtimeForm", "LoadedAssembly") &&
               HasHexSha256Field(summary, "cacheKey");
    }

    private static bool CompiledEnvelopeFieldsAbsent(SandboxAuditEvent summary)
        => !summary.Fields!.ContainsKey("artifactHash") &&
           !summary.Fields.ContainsKey("runtimeForm") &&
           !summary.Fields.ContainsKey("cacheKey");

    private static bool FieldEquals(SandboxAuditEvent summary, string key, string value)
        => summary.Fields!.TryGetValue(key, out var actual) &&
           string.Equals(actual, value, StringComparison.Ordinal);

    private static bool HasNonEmptyField(SandboxAuditEvent summary, string key)
        => summary.Fields!.TryGetValue(key, out var value) &&
           !string.IsNullOrWhiteSpace(value);

    private static bool HasHexSha256Field(SandboxAuditEvent summary, string key)
        => summary.Fields!.TryGetValue(key, out var value) && IsHexSha256(value);

    private static string ExpectedPolicyId(ExecutionPlan plan)
        => RunSummaryAuditFields.SafePolicyId(plan.Policy.PolicyId);

    private static string Format(long value)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static bool IsHexSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private readonly record struct RunSummaryField(
        string Name,
        Func<ExecutionPlan, SandboxExecutionResult, string> Expected);
}
