using System.Diagnostics;
using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost
{
    private async ValueTask<SandboxExecutionResult> ExecuteAutoAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? reusableNoAuditState = null)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return PreDispatchCancelledResult(plan, options);
        }

        if (MustInterpretAuto(plan, entrypoint, options))
        {
            return await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
        }

        AutoHotnessCompletion hotness;
        ExecutionMode selectedMode;
        // The built-in selector is sealed and its run-count decision is centralized
        // in ChooseMode, so it does not need the retainable snapshot custom selectors receive.
        if (_modeSelector is HotnessExecutionModeSelector)
        {
            var attempt = _autoHotness.BeginRunCountAttempt(plan, entrypoint);
            hotness = attempt.Completion;
            if (attempt.RunCount == 1)
            {
                return await ExecuteTrackedInterpretedAutoAsync(
                        hotness,
                        plan,
                        entrypoint,
                        input,
                        options,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            selectedMode = HotnessExecutionModeSelector.ChooseMode(options, attempt.RunCount);
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return CompleteAutoResult(hotness, PreDispatchCancelledResult(plan, options));
            }
        }
        else
        {
            var attempt = _autoHotness.BeginAttempt(plan, entrypoint);
            hotness = attempt.Completion;
            if (attempt.Stats.RunCount == 1)
            {
                return await ExecuteTrackedInterpretedAutoAsync(
                        hotness,
                        plan,
                        entrypoint,
                        input,
                        options,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!TrySelectAutoMode(
                    plan,
                    options,
                    attempt.Stats,
                    cancellationToken,
                    out selectedMode,
                    out var selectionResult))
            {
                return CompleteAutoResult(hotness, selectionResult);
            }
        }

        if (selectedMode == ExecutionMode.Interpreted ||
            !CompiledEntrypointSupport.CanCompile(plan, entrypoint))
        {
            return await ExecuteTrackedInterpretedAutoAsync(
                    hotness,
                    plan,
                    entrypoint,
                    input,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await ExecuteTrackedCompiledAutoAsync(
                hotness,
                plan,
                entrypoint,
                input,
                options,
                cancellationToken,
                reusableNoAuditState)
            .ConfigureAwait(false);
    }

    private bool MustInterpretAuto(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options)
        => !_compiled.IsAvailable ||
           options.EnableDebugTrace ||
           EntrypointHasAsyncBinding(plan, entrypoint);

    private bool TrySelectAutoMode(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats hotness,
        CancellationToken cancellationToken,
        out ExecutionMode selectedMode,
        out SandboxExecutionResult result)
    {
        ExecutionModeDecision? decision;
        try
        {
            decision = _modeSelector.Choose(
                plan,
                options,
                hotness,
                DotBoxD.Kernels.Compiler.CompiledCacheStatus.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            selectedMode = default;
            result = PreDispatchCancelledResult(plan, options);
            return false;
        }
        catch (Exception)
        {
            selectedMode = default;
            result = ExecutionModeSelectionFailureResult(plan, options);
            return false;
        }

        ThrowIfDisposed();

        if (!TryGetAutoDecisionMode(decision, out selectedMode, out var validationError))
        {
            result = InvalidExecutionOptionsResult(plan, options, validationError);
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            result = PreDispatchCancelledResult(plan, options);
            return false;
        }

        result = null!;
        return true;
    }

    private static bool TryGetAutoDecisionMode(
        ExecutionModeDecision? decision,
        out ExecutionMode selectedMode,
        out string validationError)
    {
        if (decision is null)
        {
            selectedMode = default;
            validationError = "execution mode selector returned no decision";
            return false;
        }

        selectedMode = decision.Mode;
        if (selectedMode is ExecutionMode.Interpreted or ExecutionMode.Compiled)
        {
            validationError = string.Empty;
            return true;
        }

        validationError = $"execution mode selector returned unsupported mode '{(int)selectedMode}'";
        return false;
    }

    private async ValueTask<SandboxExecutionResult> ExecuteTrackedInterpretedAutoAsync(
        AutoHotnessCompletion hotness,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken)
            .ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started);
        hotness.Complete(result, elapsed);
        return result;
    }

    private async ValueTask<SandboxExecutionResult> ExecuteTrackedCompiledAutoAsync(
        AutoHotnessCompletion hotness,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? reusableNoAuditState)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await ExecuteCompiledAsync(
                plan,
                entrypoint,
                input,
                options,
                cancellationToken,
                reusableNoAuditState)
            .ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started);
        hotness.Complete(result, elapsed);
        return result;
    }

    private static SandboxExecutionResult CompleteAutoResult(
        AutoHotnessCompletion hotness,
        SandboxExecutionResult result)
    {
        hotness.Complete(result, TimeSpan.Zero);
        return result;
    }

    private static SandboxExecutionResult ExecutionModeSelectionFailureResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var startedAt = AuditTime(plan);
        var error = new SandboxError(SandboxErrorCode.HostFailure, "execution mode selector failed");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "ExecutionModeSelectionFailed",
            startedAt,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
        WriteFailedRunSummary(audit, runId, startedAt, plan, budget, ExecutionMode.Auto, error, false);
        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.OwnedEventSnapshot(),
            ActualMode = ExecutionMode.Auto,
            ExecutionDispatched = false,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }
}
