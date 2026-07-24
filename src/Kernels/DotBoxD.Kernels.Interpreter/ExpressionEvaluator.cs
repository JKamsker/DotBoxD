using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
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
        => EvaluateCore(expression, frame, allowWidePrimitiveProbe: true);

    private ValueTask<SandboxValue> EvaluateCore(
        Expression expression,
        InterpreterFrame frame,
        bool allowWidePrimitiveProbe)
    {
        var allowWideDescendantProbe = false;
        if (!Options.EnableDebugTrace)
        {
            if (I32ExpressionEvaluator.CanEvaluate(expression, frame, _interpreter))
            {
                return new ValueTask<SandboxValue>(
                    SandboxValue.FromInt32(I32ExpressionEvaluator.Evaluate(
                        expression, frame, Context, _interpreter)));
            }

            if (TryEvaluateWidePrimitiveArithmetic(
                expression,
                frame,
                allowWidePrimitiveProbe,
                out allowWideDescendantProbe,
                out var primitive))
            {
                return new ValueTask<SandboxValue>(primitive);
            }
        }

        Context.ChargeFuel(1);
        WriteTrace(expression, frame);
        return expression switch
        {
            LiteralExpression literal => new ValueTask<SandboxValue>(ChargeLiteral(literal.Value)),
            VariableExpression variable => new ValueTask<SandboxValue>(frame.Read(variable.Name)),
            UnaryExpression unary => EvaluateUnary(unary, frame, allowWideDescendantProbe),
            BinaryExpression binary => EvaluateBinary(binary, frame, allowWideDescendantProbe),
            CallExpression call => EvaluateCall(call, frame),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported expression"))
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteTrace(Expression expression, InterpreterFrame frame)
    {
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
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryEvaluateWidePrimitiveArithmetic(
        Expression expression,
        InterpreterFrame frame,
        bool allowWidePrimitiveProbe,
        out bool allowWideDescendantProbe,
        out SandboxValue value)
    {
        if (!allowWidePrimitiveProbe)
        {
            allowWideDescendantProbe = false;
            value = null!;
            return false;
        }

        if (!IsPrimitiveArithmetic(expression))
        {
            allowWideDescendantProbe = true;
            value = null!;
            return false;
        }

        allowWideDescendantProbe = false;
        return TryEvaluateWidePrimitiveArithmeticCore(expression, frame, out value);
    }

    private bool TryEvaluateWidePrimitiveArithmeticCore(
        Expression expression,
        InterpreterFrame frame,
        out SandboxValue value)
    {
        if (!_interpreter.TryGetWideExpressionKind(expression, frame, out var kind))
        {
            value = null!;
            return false;
        }

        if (kind == WideExpressionKind.I64)
        {
            value = SandboxValue.FromInt64(I64ExpressionEvaluator.Evaluate(
                expression, frame, Context));
            return true;
        }

        value = SandboxValue.FromDouble(F64ExpressionEvaluator.Evaluate(
            expression, frame, Context));
        return true;
    }

    private static bool IsPrimitiveArithmetic(Expression expression)
        => expression is UnaryExpression { Operator: "-" } or
            BinaryExpression { Operator: "+" or "-" or "*" or "/" or "%" };

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

    public ValueTask<SandboxValue> InvokeBindingAsync(
        BindingDescriptor descriptor,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxValue arg2,
        string functionId)
        => InterpreterBindingCaller.CallAsync(
            Context,
            Options,
            ModuleHash,
            descriptor,
            arg0,
            arg1,
            arg2,
            functionId);

    private SandboxValue ChargeLiteral(SandboxValue value)
    {
        Context.ChargeValue(value);
        return value;
    }

}
