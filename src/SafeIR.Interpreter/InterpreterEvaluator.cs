namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class InterpreterEvaluator
{
    private readonly SandboxContext _context;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functions;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly SandboxExecutionOptions _options;
    private readonly string _moduleHash;
    private readonly ExpressionEvaluator _expressions;
    private readonly StatementExecutor _statements;
    private readonly Dictionary<string, FunctionFrameLayout> _frameLayouts = new(StringComparer.Ordinal);

    public InterpreterEvaluator(ExecutionPlan plan, SandboxContext context, SandboxExecutionOptions options)
    {
        _context = context;
        _options = options;
        _moduleHash = plan.ModuleHash;
        _functions = plan.FunctionLookup;
        _functionAnalysis = plan.FunctionAnalysis;
        _expressions = new ExpressionEvaluator(_context, this, _functionAnalysis, _options, _moduleHash);
        _statements = new StatementExecutor(_context, _expressions, _options, _moduleHash);
    }

    public ValueTask<SandboxValue> ExecuteEntrypointAsync(string entrypoint, SandboxValue input)
    {
        if (!_functions.TryGetValue(entrypoint, out var function) || !function.IsEntrypoint)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"entrypoint '{entrypoint}' is not available"));
        }

        _context.ChargeValue(input);
        return InvokeFunctionAsync(function, EntrypointBinder.BindArguments(function, input));
    }

    public bool TryGetFunction(string id, out SandboxFunction function) => _functions.TryGetValue(id, out function!);

    // Non-async invocation: a function whose body is fully synchronous (no pending
    // host binding) completes without ever allocating an async state machine, so a
    // helper called inside a loop costs only its indexed frame object per call.
    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
    {
        _context.EnterCall();
        var exited = false;
        try
        {
            _context.ChargeFuel(1);
            var frame = InterpreterFrame.Create(GetFrameLayout(function), function, args);
            var body = function.Body;
            for (var i = 0; i < body.Count; i++)
            {
                var statementTask = _statements.ExecuteStatementAsync(body[i], frame);
                if (!statementTask.IsCompletedSuccessfully)
                {
                    exited = true;
                    return AwaitInvoke(function, statementTask, frame, i + 1);
                }

                var result = statementTask.Result;
                if (result is not null)
                {
                    EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                    return new ValueTask<SandboxValue>(result);
                }
            }

            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"function '{function.Id}' returned no value"));
        }
        finally
        {
            if (!exited)
            {
                _context.ExitCall();
            }
        }
    }

    private async ValueTask<SandboxValue> AwaitInvoke(
        SandboxFunction function,
        ValueTask<SandboxValue?> pendingTask,
        InterpreterFrame frame,
        int nextStatement)
    {
        try
        {
            var result = await pendingTask.ConfigureAwait(false);
            if (result is not null)
            {
                EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                return result;
            }

            var body = function.Body;
            for (var i = nextStatement; i < body.Count; i++)
            {
                result = await _statements.ExecuteStatementAsync(body[i], frame).ConfigureAwait(false);
                if (result is not null)
                {
                    EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                    return result;
                }
            }

            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"function '{function.Id}' returned no value"));
        }
        finally
        {
            _context.ExitCall();
        }
    }

    // The function set is fixed for the lifetime of an evaluator, so each function's
    // local slot layout is resolved once and reused across every invocation instead
    // of rebuilding a string-keyed local map per call.
    private FunctionFrameLayout GetFrameLayout(SandboxFunction function)
    {
        if (!_frameLayouts.TryGetValue(function.Id, out var layout))
        {
            layout = FunctionFrameLayout.Build(function);
            _frameLayouts[function.Id] = layout;
        }

        return layout;
    }
}
