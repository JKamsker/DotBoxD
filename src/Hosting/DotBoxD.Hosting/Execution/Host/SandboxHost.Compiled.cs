using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost
{
    // This replaces the host's former disposed integer without changing its object size:
    // null means active without a pool, a pool means active, and this host means disposed.
    private object? _compiledNoAuditRunStates;

    private async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? reusableNoAuditState = null)
    {
        if (!_compiled.IsAvailable || options.EnableDebugTrace)
        {
            var reason = !_compiled.IsAvailable ? CompilerUnavailableError() : DebugTraceFallbackError();
            return options.AllowFallbackToInterpreter
                ? await ExecuteFallbackToInterpreterAsync(
                        plan,
                        entrypoint,
                        input,
                        options,
                        reason,
                        cancellationToken)
                    .ConfigureAwait(false)
                : CompilerUnavailableResult(plan, options, reason);
        }

        var compiled = await TryExecuteCompiledAsync(
                plan,
                entrypoint,
                input,
                options,
                cancellationToken,
                reusableNoAuditState)
            .ConfigureAwait(false);
        if (compiled.Result is not null)
        {
            return compiled.Result;
        }

        return await ExecuteFallbackToInterpreterAsync(
                plan,
                entrypoint,
                input,
                options,
                compiled.FallbackReason ?? CompilerUnavailableError(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<SandboxExecutionResult> ExecuteFallbackToInterpreterAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        SandboxError reason,
        CancellationToken cancellationToken)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var fallbackOptions = options with { RunId = runId };
        var result = await ExecuteInterpretedAsync(plan, entrypoint, input, fallbackOptions, cancellationToken)
            .ConfigureAwait(false);
        var audit = new InMemoryAuditSink();
        if (reason.Code == SandboxErrorCode.VerifierFailure)
        {
            audit.Write(VerifierFailureAudit(plan, runId, reason));
        }

        audit.Write(FallbackAudit(plan, runId, reason));
        foreach (var auditEvent in result.AuditEvents)
        {
            audit.Write(auditEvent);
        }

        return result with { AuditEvents = audit.OwnedEventSnapshot() };
    }

    private async ValueTask<CompiledAttempt> TryExecuteCompiledAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? reusableNoAuditState)
    {
        try
        {
            var executable = await GetCompiledExecutableAsync(
                    plan,
                    entrypoint,
                    options,
                    cancellationToken,
                    reusableNoAuditState)
                .ConfigureAwait(false);
            var result = await ExecuteMaterializedCompiledAsync(
                    executable,
                    plan,
                    entrypoint,
                    input,
                    options,
                    cancellationToken,
                    reusableNoAuditState)
                .ConfigureAwait(false);
            return new CompiledAttempt(result, null);
        }
        catch (SandboxRuntimeException ex) when (CanFallback(options, ex))
        {
            return new CompiledAttempt(null, ex.Error);
        }
        catch (SandboxRuntimeException ex)
        {
            return new CompiledAttempt(CompiledFailureResult(plan, options, ex.Error), null);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            return new CompiledAttempt(CompiledFailureResult(plan, options, error), null);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled execution failed");
            return new CompiledAttempt(CompiledFailureResult(plan, options, error), null);
        }
    }

    private ValueTask<SandboxExecutionResult> ExecuteMaterializedCompiledAsync(
        CompiledExecutable executable,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? reusableNoAuditState)
    {
        var useAsyncWorker = ShouldUseCompiledAsyncWorker(plan, entrypoint);
        if (useAsyncWorker)
        {
            return CompiledExecutionRunner.ExecuteOnWorkerAsync(
                executable,
                plan,
                entrypoint,
                input,
                options,
                cancellationToken);
        }

        var pooledState = TryAcquireCompiledNoAuditState(
            plan,
            entrypoint,
            executable,
            options,
            cancellationToken,
            reusableNoAuditState,
            useAsyncWorker: false);
        ValueTask<SandboxExecutionResult> execution;
        try
        {
            execution = CompiledExecutionRunner.ExecuteAsync(
                executable,
                plan,
                entrypoint,
                input,
                options,
                cancellationToken,
                ShouldUseCompiledInlineAwaitPump(plan, entrypoint),
                reusableNoAuditState ?? pooledState.State);
        }
        catch
        {
            pooledState.Dispose();
            throw;
        }

        if (execution.IsCompletedSuccessfully)
        {
            try
            {
                return ValueTask.FromResult(execution.Result);
            }
            finally
            {
                pooledState.Dispose();
            }
        }

        return AwaitAndReleaseCompiledNoAuditStateAsync(execution, pooledState);
    }

    private static async ValueTask<SandboxExecutionResult> AwaitAndReleaseCompiledNoAuditStateAsync(
        ValueTask<SandboxExecutionResult> execution,
        CompiledNoAuditRunStatePool.Lease pooledState)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            pooledState.Dispose();
        }
    }

    internal CompiledNoAuditRunStatePool.Lease TryAcquireCompiledNoAuditState(
        ExecutionPlan plan,
        string entrypoint,
        CompiledExecutable executable,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? suppliedState,
        bool useAsyncWorker)
    {
        if (suppliedState is not null ||
            useAsyncWorker ||
            cancellationToken.CanBeCanceled ||
            options.EnableDebugTrace ||
            options.Isolation != SandboxIsolation.InProcess ||
            !CompiledExecutionRunner.CanUseNoAuditSuccessPath(
                plan,
                entrypoint,
                executable.Artifact,
                options,
                out _))
        {
            return default;
        }

        return GetOrCreateCompiledNoAuditStatePool()?.TryAcquire(plan) ?? default;
    }

    internal bool HasCompiledNoAuditRunStatePool
        => Volatile.Read(ref _compiledNoAuditRunStates) is CompiledNoAuditRunStatePool;

    internal bool HasCompiledNoAuditSecondaryStateFor(ExecutionPlan plan)
        => Volatile.Read(ref _compiledNoAuditRunStates) is CompiledNoAuditRunStatePool pool &&
           pool.HasSecondaryStateFor(plan);

    private CompiledNoAuditRunStatePool? GetOrCreateCompiledNoAuditStatePool()
    {
        var current = Volatile.Read(ref _compiledNoAuditRunStates);
        if (current is CompiledNoAuditRunStatePool existing)
        {
            return existing;
        }

        if (current is not null)
        {
            return null;
        }

        var created = new CompiledNoAuditRunStatePool();
        current = Interlocked.CompareExchange(ref _compiledNoAuditRunStates, created, null);
        if (current is null)
        {
            return created;
        }

        created.Dispose();
        return current as CompiledNoAuditRunStatePool;
    }

    private bool TryDisposeCompiledNoAuditStatePool()
    {
        var lifetimeState = Interlocked.Exchange(ref _compiledNoAuditRunStates, this);
        if (ReferenceEquals(lifetimeState, this))
        {
            return false;
        }

        (lifetimeState as CompiledNoAuditRunStatePool)?.Dispose();
        return true;
    }

    private static bool CanFallback(SandboxExecutionOptions options, SandboxRuntimeException ex)
        => options.AllowFallbackToInterpreter &&
           ex.Error.Code is SandboxErrorCode.VerifierFailure or SandboxErrorCode.ValidationError;

    private static SandboxError CompilerUnavailableError()
        => new(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");

    private static SandboxError DebugTraceFallbackError()
        => new(SandboxErrorCode.ValidationError, "compiled execution is disabled while debug tracing is enabled");

    private readonly record struct CompiledAttempt(SandboxExecutionResult? Result, SandboxError? FallbackReason);
}
