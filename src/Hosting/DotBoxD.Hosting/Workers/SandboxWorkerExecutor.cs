using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal sealed class SandboxWorkerExecutor(ConfiguredSandboxWorker? worker) : IDisposable
{
    private ConfiguredSandboxWorker? _worker = worker;

    public ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(Volatile.Read(ref _worker), plan, entrypoint, input, options, cancellationToken);

    public void Dispose()
        => Volatile.Write(ref _worker, null);

    // An admitted call keeps its own worker reference while awaiting its result.
    private static async ValueTask<SandboxExecutionResult> ExecuteCoreAsync(
        ConfiguredSandboxWorker? worker,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (worker is null || !worker.Profile.SatisfiesWorkerProcess)
        {
            return Execution.SandboxHost.WorkerIsolationUnavailableResult(plan, options, worker?.Profile);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Execution.SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                WorkerCancellationOrTimeoutError(cancellationToken));
        }

        // SuppressSuccessfulRunSummaryAudit is an in-process allocation optimization only.
        // Worker-result validation (WorkerAuditMatches) structurally requires exactly one
        // RunSummary, so the worker must always emit it; suppressing it here would make every
        // successful worker run fail audit validation. Clearing the flag keeps the worker on
        // the canonical full-audit envelope and lets the validator stay strict.
        var workerOptions = options with
        {
            Isolation = SandboxIsolation.InProcess,
            SuppressSuccessfulRunSummaryAudit = false
        };
        using var timeout = CreateWorkerTimeoutSource(cancellationToken, plan.Budget.EffectiveWallTime);
        Task<SandboxExecutionResult>? pending = null;
        try
        {
            pending = worker.Client.ExecuteInWorkerAsync(
                    plan,
                    entrypoint,
                    input,
                    workerOptions,
                    timeout.Token)
                .AsTask();
            var result = await pending
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            if (timeout.IsCancellationRequested)
            {
                return Execution.SandboxHost.WorkerIsolationFailedResult(
                    plan,
                    options,
                    WorkerCancellationOrTimeoutError(cancellationToken));
            }

            return SandboxWorkerResultValidator.Validate(plan, entrypoint, options, result, out var error)
                ? result with
                {
                    AuditEvents = result.AuditEvents.ToSequencedArray(),
                    ExecutionDispatched = true
                }
                : Execution.SandboxHost.WorkerIsolationFailedResult(
                    plan,
                    options,
                    error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveLateFailure(pending);
            return Execution.SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                WorkerCancellationOrTimeoutError(cancellationToken));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            ObserveLateFailure(pending);
            return Execution.SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                WorkerCancellationOrTimeoutError(cancellationToken));
        }
        catch (Exception)
        {
            return Execution.SandboxHost.WorkerIsolationFailedResult(
                plan,
                options,
                new SandboxError(SandboxErrorCode.HostFailure, "worker process execution failed"));
        }
    }

    private static void ObserveLateFailure(Task? pending)
    {
        if (pending is null)
        {
            return;
        }
        if (pending.IsCompleted)
        {
            _ = pending.Exception;
            return;
        }

        // Cancellation stops the wait, but a worker can still complete with a failure later.
        // Fault observation must not retain the canceled waiter's ambient state.
        using var flow = ExecutionContext.SuppressFlow();
        _ = pending.ContinueWith(
            static completed => { _ = completed.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static SandboxError WorkerCancellationOrTimeoutError(CancellationToken callerToken)
        => callerToken.IsCancellationRequested
            ? new SandboxError(SandboxErrorCode.Cancelled, "worker process execution was cancelled")
            : new SandboxError(SandboxErrorCode.Timeout, "worker process execution timed out");

    private static CancellationTokenSource CreateWorkerTimeoutSource(
        CancellationToken callerToken,
        TimeSpan wallTime)
    {
        var timeout = callerToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(callerToken)
            : new CancellationTokenSource();
        timeout.CancelAfter(wallTime);
        return timeout;
    }
}
