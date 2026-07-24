using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution.Prepared;

internal static class CompiledNoAuditResultRunner
{
    public static ValueTask<SandboxExecutionResult> Execute(
        CompiledExecutable executable,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        IReadOnlySet<string> allowedBindings,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? reusableState)
    {
        var artifact = executable.Artifact;
        var budget = reusableState?.Budget ?? new ResourceMeter(plan.Budget);
        var context = reusableState?.ContextFor(allowedBindings, cancellationToken) ??
            new SandboxContext(
                SandboxRunId.Suppressed,
                plan.Policy,
                budget,
                plan.Bindings,
                NoopAuditSink.Instance,
                cancellationToken,
                allowedBindings,
                plan.ModuleHash,
                plan.PolicyHash);

        try
        {
            budget.CheckDeadline();
            context.ChargeValue(input);
            context.ClearCompiledReturnValidation();
            var value = artifact.Entrypoint(context, input);
            CompiledExecutionRunner.EnsureReturnType(
                context,
                plan,
                entrypoint,
                value,
                executable.SupportsReturnValidationProof);
            return ValueTask.FromResult(SuccessResult(plan, artifact, budget, value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            return ValueTask.FromResult(
                CompiledExecutionRunner.FailureResult(plan, executable, options, budget, error));
        }
        catch (SandboxRuntimeException ex)
        {
            return ValueTask.FromResult(
                CompiledExecutionRunner.FailureResult(plan, executable, options, budget, ex.Error));
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled sandbox execution failed");
            return ValueTask.FromResult(
                CompiledExecutionRunner.FailureResult(plan, executable, options, budget, error));
        }
        finally
        {
            context.ReleaseExecutionResources();
        }
    }

    private static SandboxExecutionResult SuccessResult(
        ExecutionPlan plan,
        CompiledArtifact artifact,
        ResourceMeter budget,
        SandboxValue value)
        => new()
        {
            Succeeded = true,
            Value = value,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = InMemoryAuditSink.EmptyEventSnapshot,
            ActualMode = ExecutionMode.Compiled,
            ExecutionDispatched = true,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash,
            ArtifactHash = artifact.ArtifactHash
        };
}
