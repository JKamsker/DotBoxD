using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost
{
    private static SandboxExecutionResult PreDispatchCancelledResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
        var audit = new InMemoryAuditSink();
        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, options.Mode, error, false);
        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = options.Mode,
            ExecutionDispatched = false,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }
}
