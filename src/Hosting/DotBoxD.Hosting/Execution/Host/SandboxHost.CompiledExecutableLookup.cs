using DotBoxD.Hosting.Execution.Prepared;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost
{
    private ValueTask<CompiledExecutable> GetCompiledExecutableAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? suppliedState)
    {
        var canUseCompletedExecutable = CanUseCompletedNoAuditExecutable(
            plan,
            entrypoint,
            options,
            cancellationToken,
            suppliedState);
        if (!canUseCompletedExecutable)
        {
            return _compiled.GetAsync(plan, entrypoint, cancellationToken);
        }

        return GetCompletedNoAuditExecutableAsync(plan, entrypoint, cancellationToken);
    }

    private ValueTask<CompiledExecutable> GetCompletedNoAuditExecutableAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        if (_compiled.TryGetCompletedExecutable(plan, entrypoint, out var executable) &&
            executable.Artifact.CacheInvalidReason is null)
        {
            return ValueTask.FromResult(executable);
        }

        if (!_compiled.CanPublishCompletedExecutable)
        {
            if (_compiled.TryGetCachedCompletedExecutable(plan, entrypoint, out executable) &&
                executable.Artifact.CacheInvalidReason is null)
            {
                return ValueTask.FromResult(executable);
            }

            return _compiled.GetAsync(plan, entrypoint, cancellationToken);
        }

        return _compiled.GetAndPublishCompletedExecutableAsync(
            plan,
            entrypoint,
            cancellationToken);
    }

    private static bool CanUseCompletedNoAuditExecutable(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? suppliedState)
    {
        if (suppliedState is not null ||
            cancellationToken.CanBeCanceled ||
            options.EnableDebugTrace ||
            options.Isolation != SandboxIsolation.InProcess ||
            !options.SuppressSuccessfulRunSummaryAudit)
        {
            return false;
        }

        return !ShouldUseCompiledAsyncWorker(plan, entrypoint) &&
               plan.BindingReferences.TryGetValue(entrypoint, out var allowedBindings) &&
               allowedBindings.Count == 0;
    }
}
