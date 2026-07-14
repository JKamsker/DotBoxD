using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

internal static class InterpreterExecutionBoundary
{
    private const string FailureMessage = "interpreter execution failed";

    public static async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ISandboxInterpreter interpreter,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await interpreter.ExecuteAsync(
                    plan,
                    entrypoint,
                    input,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result is null ||
                !InterpreterResultValidator.IsValid(plan, entrypoint, options, result))
            {
                return FailureResult(
                    plan,
                    options,
                    new SandboxError(SandboxErrorCode.HostFailure, FailureMessage));
            }

            return cancellationToken.IsCancellationRequested
                ? InterpreterCancellationBoundary.CancelledResult(plan, options, result)
                : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FailureResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled"));
        }
        catch (Exception)
        {
            return FailureResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.HostFailure, FailureMessage));
        }
    }

    private static SandboxExecutionResult FailureResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxError error)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var audit = new InMemoryAuditSink();
        WriteRunSummary(audit, runId, AuditTime(plan), plan, budget, error);
        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static void WriteRunSummary(
        InMemoryAuditSink audit,
        SandboxRunId runId,
        DateTimeOffset startedAt,
        ExecutionPlan plan,
        ResourceMeter budget,
        SandboxError error)
    {
        var fields = RunSummaryAuditFields.Create(
            plan,
            budget,
            ExecutionMode.Interpreted,
            "None");
        audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: $"mode=interpreted cacheStatus=None plan={plan.PlanHash} " +
                     $"policy={plan.PolicyHash} policyId={fields["policyId"]} " +
                     $"bindings={plan.BindingManifestHash} " +
                     $"fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}",
            Fields: fields));
    }

    private static DateTimeOffset AuditTime(ExecutionPlan plan)
        => plan.Policy.Deterministic
            ? plan.Policy.LogicalNow ?? DateTimeOffset.UnixEpoch
            : DateTimeOffset.UtcNow;
}
