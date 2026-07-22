using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

using DotBoxD.Kernels;

public sealed class SandboxInterpreter : ISandboxInterpreter
{
    private readonly ConditionalWeakTable<ExecutionPlan, FunctionFrameLayoutCache> _frameLayouts = new();

    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(entrypoint);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        var budget = new ResourceMeter(plan.Budget);
        plan.BindingReferences.TryGetValue(entrypoint, out var allowedBindings);
        var context = CanInitializeAuditEnvelopeLazily(options, allowedBindings)
            ? SandboxContext.CreateWithLazyAuditEnvelope(
                options.RunId,
                plan.Policy,
                budget,
                plan.Bindings,
                cancellationToken,
                allowedBindings,
                plan.ModuleHash,
                plan.PolicyHash)
            : new SandboxContext(
                options.RunId ?? SandboxRunId.New(),
                plan.Policy,
                budget,
                plan.Bindings,
                new InMemoryAuditSink(),
                cancellationToken,
                allowedBindings,
                plan.ModuleHash,
                plan.PolicyHash);
        var startedAt = AuditTime(plan);

        try
        {
            budget.CheckDeadline();
            InterpreterNestingGuard.ThrowIfExceeded(plan);
            var frameLayouts = _frameLayouts.GetValue(plan, static value => new FunctionFrameLayoutCache(value));
            var evaluator = new InterpreterEvaluator(plan, context, options, frameLayouts);
            var value = await evaluator.ExecuteEntrypointAsync(entrypoint, input).ConfigureAwait(false);
            if (!options.SuppressSuccessfulRunSummaryAudit)
            {
                WriteSummary(context, startedAt, plan, budget, true, null);
            }

            return Result(plan, budget, context, true, value, null);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            WriteSummary(context, startedAt, plan, budget, false, error);
            return Result(plan, budget, context, false, null, error);
        }
        catch (SandboxRuntimeException ex)
        {
            WriteSummary(context, startedAt, plan, budget, false, ex.Error);
            return Result(plan, budget, context, false, null, ex.Error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "sandbox execution failed");
            WriteSummary(context, startedAt, plan, budget, false, error);
            return Result(plan, budget, context, false, null, error);
        }
        finally
        {
            context.Dispose();
        }
    }

    private static SandboxExecutionResult Result(
        ExecutionPlan plan,
        ResourceMeter budget,
        SandboxContext context,
        bool succeeded,
        SandboxValue? value,
        SandboxError? error)
        => new()
        {
            Succeeded = succeeded,
            Value = value,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = context.AuditIfCreated is InMemoryAuditSink audit
                ? audit.SnapshotEvents()
                : InMemoryAuditSink.EmptyEventSnapshot,
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };

    private static void WriteSummary(
        SandboxContext context,
        DateTimeOffset startedAt,
        ExecutionPlan plan,
        ResourceMeter budget,
        bool success,
        SandboxError? error)
    {
        var fields = RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None");
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "RunSummary",
            startedAt,
            success,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error?.Code,
            Message: $"mode=interpreted cacheStatus=None plan={plan.PlanHash} " +
                     $"policy={plan.PolicyHash} policyId={fields["policyId"]} bindings={plan.BindingManifestHash} " +
                     $"fuel={budget.FuelUsed}/{budget.Limits.MaxFuel}",
            Fields: fields));
    }

    private static bool CanInitializeAuditEnvelopeLazily(
        SandboxExecutionOptions options,
        IReadOnlySet<string>? allowedBindings)
        => options.SuppressSuccessfulRunSummaryAudit &&
           !options.EnableDebugTrace &&
           options.Isolation == SandboxIsolation.InProcess &&
           allowedBindings is { Count: 0 };

    private static DateTimeOffset AuditTime(ExecutionPlan plan)
        => plan.Policy.Deterministic
            ? plan.Policy.LogicalNow ?? DateTimeOffset.UnixEpoch
            : DateTimeOffset.UtcNow;
}
