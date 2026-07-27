using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

/// <summary>
/// Executes the same narrow one-argument I32 helper shape used by loop plans without
/// materializing the helper's frame. Metering stays incremental so cancellation,
/// quota, and arithmetic-fault order match ordinary local-function dispatch.
/// </summary>
internal static class InlineI32LocalFunctionCallEvaluator
{
    public static bool TryEvaluate(
        Expression expression,
        InterpreterFrame frame,
        InterpreterEvaluator interpreter,
        out int value,
        out SandboxFunction? genericFunction)
    {
        value = 0;
        genericFunction = null;
        if (interpreter.Options.EnableDebugTrace ||
            expression is not CallExpression call ||
            !interpreter.TryGetInlineI32LocalFunctionCallPlan(
                call,
                out var plan,
                out genericFunction) ||
            !plan.CanEvaluateArgument(frame))
        {
            return false;
        }

        value = plan.Evaluate(frame, interpreter);
        return true;
    }

    internal static bool TryCreatePlan(
        CallExpression call,
        InterpreterEvaluator interpreter,
        out InlineI32LocalFunctionCallPlan plan,
        out SandboxFunction? genericFunction)
    {
        plan = null!;
        genericFunction = null;
        if (!ExpressionEvaluator.CanReuseResolvedLocalCall(call) ||
            !interpreter.TryGetFunction(call.Name, out var function))
        {
            return false;
        }

        genericFunction = function;
        if (I32LocalFunctionAnalyzer.TryGetInlineableReturn(function, call, out var body) &&
            CanEvaluateBody(body, function.Parameters[0].Name))
        {
            plan = new InlineI32LocalFunctionCallPlan(
                call.Arguments[0],
                body,
                function.Parameters[0].Name);
            return true;
        }

        return false;
    }

    private static bool CanEvaluateBody(Expression expression, string parameterName)
        => expression switch
        {
            LiteralExpression { Value: I32Value } => true,
            VariableExpression variable =>
                string.Equals(variable.Name, parameterName, StringComparison.Ordinal),
            UnaryExpression { Operator: "-" } unary =>
                CanEvaluateBody(unary.Operand, parameterName),
            BinaryExpression binary when IsSupportedBinaryOperator(binary.Operator) =>
                CanEvaluateBody(binary.Left, parameterName) &&
                CanEvaluateBody(binary.Right, parameterName),
            _ => false
        };

    private static bool IsSupportedBinaryOperator(string op)
        => op is "+" or "-" or "*" or "/" or "%";
}

internal sealed class InlineI32LocalFunctionCallPlan(
    Expression argument,
    Expression body,
    string parameterName)
{
    public bool CanEvaluateArgument(InterpreterFrame frame)
    {
        if (argument is VariableExpression variable &&
            !frame.TryGetSlot(variable.Name, out _))
        {
            return false;
        }

        return I32ExpressionEvaluator.CanEvaluate(argument, frame);
    }

    public int Evaluate(InterpreterFrame frame, InterpreterEvaluator interpreter)
    {
        var context = interpreter.Context;
        context.ChargeFuel(1);
        var argumentValue = I32ExpressionEvaluator.Evaluate(
            argument,
            frame,
            context,
            interpreter);
        context.EnterCall();
        try
        {
            context.ChargeFuel(1);
            context.ChargeFuel(1);
            return EvaluateBody(body, argumentValue, context);
        }
        finally
        {
            context.ExitCall();
        }
    }

    private int EvaluateBody(
        Expression expression,
        int parameterValue,
        SandboxContext context)
    {
        context.ChargeFuel(1);
        return expression switch
        {
            LiteralExpression { Value: I32Value value } => value.Value,
            VariableExpression variable when
                string.Equals(variable.Name, parameterName, StringComparison.Ordinal) => parameterValue,
            UnaryExpression { Operator: "-" } unary => SandboxInt32Math.Negate(
                EvaluateBody(unary.Operand, parameterValue, context)),
            BinaryExpression binary => EvaluateBinary(binary, parameterValue, context),
            _ => throw Unsupported()
        };
    }

    private int EvaluateBinary(
        BinaryExpression binary,
        int parameterValue,
        SandboxContext context)
    {
        var left = EvaluateBody(binary.Left, parameterValue, context);
        var right = EvaluateBody(binary.Right, parameterValue, context);
        return binary.Operator switch
        {
            "+" => SandboxInt32Math.Add(left, right),
            "-" => SandboxInt32Math.Subtract(left, right),
            "*" => SandboxInt32Math.Multiply(left, right),
            "/" => SandboxInt32Math.Divide(left, right),
            "%" => SandboxInt32Math.Remainder(left, right),
            _ => throw Unsupported()
        };
    }

    private static SandboxRuntimeException Unsupported()
        => new(new SandboxError(
            SandboxErrorCode.ValidationError,
            "unsupported i32 expression"));
}
