using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

using DotBoxD.Kernels;

internal sealed class InterpreterEvaluator : I32CallEvaluator
{
    private readonly SandboxContext _context;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functions;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly SandboxExecutionOptions _options;
    private readonly ExecutionPlan _plan;
    private readonly ExpressionEvaluator _expressions;
    private readonly StatementExecutor _statements;
    private readonly FunctionFrameLayoutCache _frameLayouts;
    private SandboxFunction? _lastFrameLayoutFunction;
    private FunctionFrameLayout? _lastFrameLayout;

    public InterpreterEvaluator(
        ExecutionPlan plan,
        SandboxContext context,
        SandboxExecutionOptions options,
        FunctionFrameLayoutCache frameLayouts)
    {
        _context = context;
        _options = options;
        _plan = plan;
        _functions = plan.FunctionLookup;
        _functionAnalysis = plan.FunctionAnalysis;
        _frameLayouts = frameLayouts;
        _expressions = new ExpressionEvaluator(this);
        _statements = new StatementExecutor(this);
    }

    internal SandboxContext Context => _context;

    internal IReadOnlyDictionary<string, FunctionAnalysis> FunctionAnalysis => _functionAnalysis;

    internal SandboxExecutionOptions Options => _options;

    internal string ModuleHash => _plan.ModuleHash;

    internal ExpressionEvaluator Expressions => _expressions;

    internal bool TryGetWideExpressionKind(
        Expression expression,
        InterpreterFrame frame,
        out WideExpressionKind kind)
        => _frameLayouts.TryGetWideExpressionKind(expression, frame, out kind);

    public ValueTask<SandboxValue> ExecuteEntrypointAsync(string entrypoint, SandboxValue input)
    {
        if (!_functions.TryGetValue(entrypoint, out var function) || !function.IsEntrypoint)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"entrypoint '{entrypoint}' is not available"));
        }

        _context.ChargeValue(input);
        ValidateEntrypointArguments(function, input);
        return InvokeFunctionCoreAsync(function, arguments: default, entrypointInput: input);
    }

    public bool TryGetFunction(string id, out SandboxFunction function) => _functions.TryGetValue(id, out function!);

    public bool CanEvaluateInt32Call(CallExpression call)
        => call.Arguments.Count == 0 &&
           _functions.TryGetValue(call.Name, out var function) &&
           function.Parameters.Count == 0 &&
           function.ReturnType == SandboxType.I32 &&
           I32LocalFunctionAnalyzer.TryGetConstantReturn(function, out _);

    public int EvaluateInt32Call(CallExpression call)
    {
        if (!_functions.TryGetValue(call.Name, out var function) ||
            !I32LocalFunctionAnalyzer.TryGetConstantReturn(function, out var expression))
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"unknown call '{call.Name}' at runtime"));
        }

        _context.EnterCall();
        try
        {
            _context.ChargeFuel(1);
            _context.ChargeFuel(1);
            return I32ExpressionEvaluator.Evaluate(expression, frame: null, _context, calls: null);
        }
        finally
        {
            _context.ExitCall();
        }
    }

    public bool TryCreateInt32CallPlan(
        CallExpression call,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ExpressionPlan plan)
    {
        plan = null!;
        if (!_functions.TryGetValue(call.Name, out var function) ||
            !I32LocalFunctionAnalyzer.TryGetInlineableReturn(function, call, out var expression))
        {
            return false;
        }

        var parameter = function.Parameters[0];
        if (!I32ExpressionPlan.TryCreate(call.Arguments[0], frame, assumedInt32Local, this, out var argument))
        {
            return false;
        }

        var substitution = new I32ExpressionSubstitution(parameter.Name, argument);
        if (!I32ExpressionPlan.TryCreate(
            expression,
            frame,
            assumedInt32Local,
            calls: null,
            substitution,
            out var body))
        {
            return false;
        }

        plan = I32ExpressionPlan.InlineCall(body);
        return true;
    }

    // Non-async invocation: a function whose body is fully synchronous (no pending
    // host binding) completes without ever allocating an async state machine, so a
    // helper called inside a loop costs only its indexed frame object per call.
    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, LocalFunctionArguments args)
        => InvokeFunctionCoreAsync(function, args, entrypointInput: null);

    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, LocalFunctionTripleArguments args)
        => InvokeTripleFunctionCoreAsync(function, args);

    private ValueTask<SandboxValue> InvokeFunctionCoreAsync(
        SandboxFunction function,
        LocalFunctionArguments arguments,
        SandboxValue? entrypointInput)
    {
        _context.EnterCall();
        var exited = false;
        try
        {
            _context.ChargeFuel(1);
            var layout = GetFrameLayout(function);
            var frame = entrypointInput is null
                ? InterpreterFrame.Create(layout, function, arguments)
                : InterpreterFrame.CreateValidatedEntrypoint(layout, function, entrypointInput);
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

    private ValueTask<SandboxValue> InvokeTripleFunctionCoreAsync(
        SandboxFunction function,
        LocalFunctionTripleArguments arguments)
    {
        // This overload deliberately leaves the existing one/two-argument hot path unchanged.
        _context.EnterCall();
        var exited = false;
        try
        {
            _context.ChargeFuel(1);
            var layout = GetFrameLayout(function);
            var frame = InterpreterFrameBuilder.Create(layout, function, arguments);
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

    private static void ValidateEntrypointArguments(SandboxFunction function, SandboxValue input)
    {
        var parameterCount = function.Parameters.Count;
        EntrypointBinder.ValidateInputShape(input, parameterCount);
        for (var i = 0; i < parameterCount; i++)
        {
            _ = EntrypointBinder.GetArgument(input, i, parameterCount, function.Parameters[i].Type);
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

    // The function set is fixed for the prepared plan. Keep the most recently used
    // layout on the evaluator for tight helper-call loops, while the shared cache
    // preserves layouts across separate executions of the same plan.
    private FunctionFrameLayout GetFrameLayout(SandboxFunction function)
    {
        if (ReferenceEquals(function, _lastFrameLayoutFunction))
        {
            return _lastFrameLayout!;
        }

        var layout = _frameLayouts.Get(function, _plan);
        _lastFrameLayoutFunction = function;
        _lastFrameLayout = layout;
        return layout;
    }

}
