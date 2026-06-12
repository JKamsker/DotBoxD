namespace SafeIR.Hosting;

using SafeIR;

internal sealed class SandboxWorkerExecutor(ConfiguredSandboxWorker? worker)
{
    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (worker is null || !worker.Profile.SatisfiesWorkerProcess)
        {
            return SandboxHost.WorkerIsolationUnavailableResult(plan, options, worker?.Profile);
        }

        var workerOptions = options with { Isolation = SandboxIsolation.InProcess };
        try
        {
            var result = await worker.Client.ExecuteInWorkerAsync(
                    plan,
                    entrypoint,
                    input,
                    workerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return WorkerResultMatches(plan, result)
                ? result
                : SandboxHost.WorkerIsolationFailedResult(
                    plan,
                    options,
                    new SandboxError(SandboxErrorCode.HostFailure, "worker result identity did not match the requested plan"));
        }
        catch (OperationCanceledException)
        {
            return SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.Cancelled, "worker process execution was cancelled"));
        }
        catch (Exception)
        {
            return SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.HostFailure, "worker process execution failed"));
        }
    }

    private static bool WorkerResultMatches(ExecutionPlan plan, SandboxExecutionResult result)
        => string.Equals(result.ModuleHash, plan.ModuleHash, StringComparison.Ordinal) &&
           string.Equals(result.PlanHash, plan.PlanHash, StringComparison.Ordinal) &&
           string.Equals(result.PolicyHash, plan.PolicyHash, StringComparison.Ordinal) &&
           WorkerModeMatches(result);

    private static bool WorkerModeMatches(SandboxExecutionResult result)
    {
        if (!Enum.IsDefined(result.ActualMode) || result.ActualMode == ExecutionMode.Auto)
        {
            return false;
        }

        if (result.ActualMode == ExecutionMode.Interpreted)
        {
            return string.IsNullOrWhiteSpace(result.ArtifactHash);
        }

        return !result.Succeeded || !string.IsNullOrWhiteSpace(result.ArtifactHash);
    }
}
