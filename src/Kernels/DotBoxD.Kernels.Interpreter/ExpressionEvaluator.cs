using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

using DotBoxD.Kernels;

internal readonly partial struct ExpressionEvaluator
{
    private readonly InterpreterEvaluator _interpreter;

    public ExpressionEvaluator(InterpreterEvaluator interpreter) =>
        _interpreter = interpreter;

    private SandboxContext Context => _interpreter.Context;

    private IReadOnlyDictionary<string, FunctionAnalysis> FunctionAnalysis =>
        _interpreter.FunctionAnalysis;

    private SandboxExecutionOptions Options => _interpreter.Options;

    private string ModuleHash => _interpreter.ModuleHash;

    // Non-async dispatch: literals, variables, arithmetic, and pure helper calls all
    // complete synchronously, so they return a finished ValueTask without ever
    // allocating an async state machine. Only genuinely asynchronous work (a host
    // binding whose ValueTask is still pending) walks the async continuation path.
    public ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
    {
        if (!Options.EnableDebugTrace &&
            I32ExpressionEvaluator.CanEvaluate(expression, frame, _interpreter))
        {
            return new ValueTask<SandboxValue>(
                SandboxValue.FromInt32(I32ExpressionEvaluator.Evaluate(
                    expression, frame, Context, _interpreter)));
        }

        Context.ChargeFuel(1);
        if (Options.EnableDebugTrace)
        {
            InterpreterTrace.Write(
                Context,
                Options,
                ModuleHash,
                frame.FunctionId,
                "expression",
                expression.GetType().Name,
                expression.Span);
        }

        return expression switch
        {
            LiteralExpression literal => new ValueTask<SandboxValue>(ChargeLiteral(literal.Value)),
            VariableExpression variable => new ValueTask<SandboxValue>(frame.Read(variable.Name)),
            UnaryExpression unary => EvaluateUnary(unary, frame),
            BinaryExpression binary => EvaluateBinary(binary, frame),
            CallExpression call => EvaluateCall(call, frame),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported expression"))
        };
    }

    public bool TryEvaluateInt32(Expression expression, InterpreterFrame frame, out int value)
    {
        if (!Options.EnableDebugTrace &&
            I32ExpressionEvaluator.CanEvaluate(expression, frame, _interpreter))
        {
            value = I32ExpressionEvaluator.Evaluate(
                expression, frame, Context, _interpreter);
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryEvaluateInt64(Expression expression, InterpreterFrame frame, out long value)
    {
        if (!Options.EnableDebugTrace)
        {
            if (I64ExpressionEvaluator.CanEvaluate(expression, frame))
            {
                value = I64ExpressionEvaluator.Evaluate(expression, frame, Context);
                return true;
            }

            if (TryEvaluateInt32ToInt64(expression, frame, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    public bool TryEvaluateDouble(Expression expression, InterpreterFrame frame, out double value)
    {
        if (!Options.EnableDebugTrace)
        {
            if (F64ExpressionEvaluator.CanEvaluate(expression, frame))
            {
                value = F64ExpressionEvaluator.Evaluate(expression, frame, Context);
                return true;
            }

            if (TryEvaluateToDouble(expression, frame, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private bool TryEvaluateInt32ToInt64(
        Expression expression,
        InterpreterFrame frame,
        out long value)
    {
        if (expression is CallExpression { Name: "numeric.toI64", Arguments.Count: 1 } call &&
            I32ExpressionEvaluator.CanEvaluate(call.Arguments[0], frame, _interpreter))
        {
            Context.ChargeFuel(1);
            value = I32ExpressionEvaluator.Evaluate(
                call.Arguments[0], frame, Context, _interpreter);
            return true;
        }

        value = 0;
        return false;
    }

    private bool TryEvaluateToDouble(
        Expression expression,
        InterpreterFrame frame,
        out double value)
    {
        if (expression is not CallExpression { Name: "numeric.toF64", Arguments.Count: 1 } call)
        {
            value = 0;
            return false;
        }

        var operand = call.Arguments[0];
        if (I32ExpressionEvaluator.CanEvaluate(operand, frame, _interpreter))
        {
            Context.ChargeFuel(1);
            value = I32ExpressionEvaluator.Evaluate(
                operand, frame, Context, _interpreter);
            return true;
        }

        if (I64ExpressionEvaluator.CanEvaluate(operand, frame))
        {
            Context.ChargeFuel(1);
            value = I64ExpressionEvaluator.Evaluate(operand, frame, Context);
            return true;
        }

        value = 0;
        return false;
    }

    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, LocalFunctionArguments args)
        => _interpreter.InvokeFunctionAsync(function, args);

    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, LocalFunctionTripleArguments args)
        => _interpreter.InvokeFunctionAsync(function, args);

    public ValueTask<SandboxValue> InvokeBindingAsync(
        BindingDescriptor descriptor,
        SandboxValue arg0,
        string functionId)
        => InterpreterBindingCaller.CallAsync(
            Context,
            Options,
            ModuleHash,
            descriptor,
            arg0,
            functionId);

    public ValueTask<SandboxValue> InvokeBindingAsync(
        BindingDescriptor descriptor,
        SandboxValue arg0,
        SandboxValue arg1,
        string functionId)
        => InterpreterBindingCaller.CallAsync(
            Context,
            Options,
            ModuleHash,
            descriptor,
            arg0,
            arg1,
            functionId);

    private SandboxValue ChargeLiteral(SandboxValue value)
    {
        Context.ChargeValue(value);
        return value;
    }

}
