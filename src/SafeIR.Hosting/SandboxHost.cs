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
        if (ShouldUseCompiler(options)) {
            var compiled = await TryExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken).ConfigureAwait(false);
            if (compiled is not null) {
                return compiled;
            }
        }

        return await _interpreter.ExecuteAsync(plan, entrypoint, input, options, cancellationToken).ConfigureAwait(false);
    }

    private bool ShouldUseCompiler(SandboxExecutionOptions options)
        => _compiler is not null &&
           !options.EnableDebugTrace &&
           options.Mode is ExecutionMode.Compiled or ExecutionMode.Auto;

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
        => options.Mode == ExecutionMode.Auto ||
           (options.AllowFallbackToInterpreter && ex.Error.Code is SandboxErrorCode.VerifierFailure or SandboxErrorCode.ValidationError);
}
