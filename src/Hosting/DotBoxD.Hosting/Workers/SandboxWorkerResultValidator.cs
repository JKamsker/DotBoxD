using DotBoxD.Hosting.Worker;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal static class SandboxWorkerResultValidator
{
    private static readonly ResourceUsageLimit[] WorkerResourceUsageLimits =
    [
        new(static usage => usage.FuelUsed, static budget => budget.MaxFuel),
        new(static usage => usage.LoopIterations, static budget => budget.MaxLoopIterations),
        new(static usage => usage.AllocatedBytes, static budget => budget.MaxAllocatedBytes),
        new(static usage => usage.HostCalls, static budget => budget.MaxHostCalls),
        new(static usage => usage.FileBytesRead, static budget => budget.MaxFileBytesRead),
        new(static usage => usage.FileBytesWritten, static budget => budget.MaxFileBytesWritten),
        new(static usage => usage.NetworkBytesRead, static budget => budget.MaxNetworkBytesRead),
        new(static usage => usage.NetworkBytesWritten, static budget => budget.MaxNetworkBytesWritten),
        new(static usage => usage.LogEvents, static budget => budget.MaxLogEvents),
        new(static usage => usage.CollectionElements, static budget => budget.MaxTotalCollectionElements),
        new(static usage => usage.StringBytes, static budget => budget.MaxTotalStringBytes),
    ];

    public static bool Validate(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        out SandboxError error)
    {
        error = new SandboxError(SandboxErrorCode.HostFailure, "worker result identity did not match the requested plan");
        if (!string.Equals(result.ModuleHash, plan.ModuleHash, StringComparison.Ordinal) ||
            !string.Equals(result.PlanHash, plan.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(result.PolicyHash, plan.PolicyHash, StringComparison.Ordinal))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker result mode did not match the requested execution mode");
        if (!WorkerModeMatches(options, result))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker result payload was malformed");
        if (!WorkerPayloadMatches(plan, entrypoint, result, out var resultShapeUsage))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker resource usage was malformed");
        if (!WorkerResourceUsageMatches(plan, result, resultShapeUsage))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker audit envelope was malformed");
        if (!WorkerAuditMatches(plan, entrypoint, options, result))
        {
            return false;
        }

        error = new SandboxError(SandboxErrorCode.HostFailure, "worker deterministic binding result did not match audit evidence");
        return SandboxWorkerDeterministicBindingValidator.Matches(plan, entrypoint, result);
    }

    private static bool WorkerModeMatches(SandboxExecutionOptions options, SandboxExecutionResult result)
    {
        if (!Enum.IsDefined(result.ActualMode) || result.ActualMode == ExecutionMode.Auto)
        {
            return false;
        }

        if (options.Mode == ExecutionMode.Interpreted && result.ActualMode != ExecutionMode.Interpreted)
        {
            return false;
        }

        if (!RequestedWorkerModeMatches(options, result))
        {
            return false;
        }

        return WorkerArtifactHashMatches(result);
    }

    private static bool RequestedWorkerModeMatches(SandboxExecutionOptions options, SandboxExecutionResult result)
    {
        if (options.Mode == ExecutionMode.Compiled &&
            !options.AllowFallbackToInterpreter &&
            result.ActualMode != ExecutionMode.Compiled)
        {
            return false;
        }

        return true;
    }

    private static bool WorkerArtifactHashMatches(SandboxExecutionResult result)
    {
        if (result.ActualMode == ExecutionMode.Interpreted)
        {
            return string.IsNullOrWhiteSpace(result.ArtifactHash);
        }

        return result.Succeeded
            ? WorkerRunSummaryValidator.IsHexSha256(result.ArtifactHash)
            : string.IsNullOrWhiteSpace(result.ArtifactHash) || WorkerRunSummaryValidator.IsHexSha256(result.ArtifactHash);
    }

    private static bool WorkerPayloadMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionResult result,
        out SandboxResourceUsage? resultShapeUsage)
    {
        resultShapeUsage = null;
        if (result.Succeeded)
        {
            if (result.Value is null || result.Error is not null)
            {
                return false;
            }

            if (!plan.FunctionAnalysis.TryGetValue(entrypoint, out var analysis))
            {
                return false;
            }

            try
            {
                resultShapeUsage = WorkerResultShapeUsage.Measure(result.Value, analysis.ReturnType, plan.Budget);
            }
            catch (SandboxRuntimeException)
            {
                return false;
            }

            return true;
        }

        return result.Value is null &&
               WorkerEnvelopeValidators.ErrorMatches(result.Error);
    }

    private static bool WorkerResourceUsageMatches(
        ExecutionPlan plan,
        SandboxExecutionResult result,
        SandboxResourceUsage? resultShapeUsage)
    {
        var usage = result.ResourceUsage;
        return usage.MaxFuel == plan.Budget.MaxFuel &&
            ResourceUsageWithinLimits(usage, plan.Budget) &&
            WorkerResultShapeUsageMatches(usage, resultShapeUsage);
    }

    private static bool ResourceUsageWithinLimits(SandboxResourceUsage usage, ResourceLimits budget)
    {
        foreach (var limit in WorkerResourceUsageLimits)
        {
            var value = limit.Value(usage);
            if (value < 0 || value > limit.Maximum(budget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool WorkerResultShapeUsageMatches(
        SandboxResourceUsage usage,
        SandboxResourceUsage? resultShapeUsage)
        => resultShapeUsage is not { } shape ||
           (usage.FuelUsed >= shape.FuelUsed &&
            usage.AllocatedBytes >= shape.AllocatedBytes &&
            usage.CollectionElements >= shape.CollectionElements &&
            usage.StringBytes >= shape.StringBytes);

    private static bool WorkerAuditMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result)
    {
        if (result.AuditEvents.Count == 0)
        {
            return false;
        }

        var runId = result.AuditEvents[0].RunId;
        if (options.RunId is not null && options.RunId != runId)
        {
            return false;
        }

        if (!SandboxWorkerAuditEvidenceCollector.TryCollect(plan, entrypoint, options, result, runId, out var evidence) ||
            !AuditSummaryMatchesResult(result, evidence))
        {
            return false;
        }

        return WorkerRunSummaryValidator.RunSummaryMatches(plan, result, evidence.Summary) &&
            ResultErrorMatches(result, evidence.Summary);
    }
    private static bool AuditSummaryMatchesResult(SandboxExecutionResult result, SandboxWorkerAuditEvidence evidence)
    {
        if (evidence.Summary.Success != result.Succeeded)
        {
            return false;
        }

        return SandboxWorkerAuditEvidenceCollector.UsageMatches(result.ResourceUsage, evidence);
    }

    private static bool ResultErrorMatches(SandboxExecutionResult result, SandboxAuditEvent summary)
    {
        if (result.Succeeded)
        {
            return summary.ErrorCode is null;
        }

        if (summary.ErrorCode != result.Error!.Code)
        {
            return false;
        }

        return summary.ErrorCode is { } code && Enum.IsDefined(code);
    }

    private readonly record struct ResourceUsageLimit(Func<SandboxResourceUsage, long> Value, Func<ResourceLimits, long> Maximum);
}
