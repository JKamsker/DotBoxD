namespace SafeIR.Hosting;

using SafeIR;
using SafeIR.Compiler;
using SafeIR.Interpreter;
using SafeIR.Validation;

public sealed class SandboxHost
{
    private readonly BindingRegistry _bindings;
    private readonly ISandboxInterpreter _interpreter;
    private readonly ISandboxCompiler? _compiler;

    internal SandboxHost(BindingRegistry bindings, ISandboxInterpreter interpreter, ISandboxCompiler? compiler)
    {
        _bindings = bindings;
        _interpreter = interpreter;
        _compiler = compiler;
    }

    public static SandboxHost Create(Action<SandboxHostBuilder>? configure = null)
    {
        var builder = new SandboxHostBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    public ValueTask<SandboxModule> ParseJsonAsync(string jsonIr, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SafeIrJsonImporter.Import(jsonIr));
    }

    public ValueTask<ExecutionPlan> PrepareAsync(
        SandboxModule module,
        SandboxPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = new ModuleValidator().Validate(module, _bindings, policy);
        if (!validation.Succeeded) {
            throw new SandboxValidationException(validation.Diagnostics);
        }

        return ValueTask.FromResult(ExecutionPlanBuilder.Build(module, policy, _bindings, validation.Functions));
    }

    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SandboxExecutionOptions();
        if (options.RequireDeterministic && !plan.Policy.Deterministic) {
            return DeterminismRequiredResult(plan, options);
        }

        return options.Mode switch {
            ExecutionMode.Compiled => await ExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false),
            ExecutionMode.Interpreted or ExecutionMode.Auto => await ExecuteInterpretedAsync(
                    plan,
                    entrypoint,
                    input,
                    options,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => CompilerUnavailableResult(plan, options)
        };
    }

    private async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (_compiler is null || options.EnableDebugTrace) {
            return options.AllowFallbackToInterpreter
                ? await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken).ConfigureAwait(false)
                : CompilerUnavailableResult(plan, options);
        }

        var compiled = await TryExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken).ConfigureAwait(false);
        if (compiled is not null) {
            return compiled;
        }

        return await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SandboxExecutionResult> ExecuteInterpretedAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
        => await _interpreter.ExecuteAsync(plan, entrypoint, input, options, cancellationToken).ConfigureAwait(false);

    private async ValueTask<SandboxExecutionResult?> TryExecuteCompiledAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        try {
            var artifact = await _compiler!.CompileAsync(plan, new CompileOptions(entrypoint), cancellationToken).ConfigureAwait(false);
            return await CompiledExecutionRunner.ExecuteAsync(artifact, plan, input, options, cancellationToken).ConfigureAwait(false);
        }
        catch (SandboxRuntimeException ex) when (CanFallback(options, ex)) {
            return null;
        }
    }

    private static bool CanFallback(SandboxExecutionOptions options, SandboxRuntimeException ex)
        => options.AllowFallbackToInterpreter &&
           ex.Error.Code is SandboxErrorCode.VerifierFailure or SandboxErrorCode.ValidationError;

    private static SandboxExecutionResult CompilerUnavailableResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var error = new SandboxError(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CompilerUnavailable",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));

        return new SandboxExecutionResult {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Compiled,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult DeterminismRequiredResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var error = new SandboxError(SandboxErrorCode.PolicyDenied, "deterministic execution is required");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "PolicyDenied",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));

        return new SandboxExecutionResult {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = options.Mode,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }
}
