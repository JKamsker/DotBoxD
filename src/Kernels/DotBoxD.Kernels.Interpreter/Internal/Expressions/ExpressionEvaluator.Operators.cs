using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal readonly partial struct ExpressionEvaluator
{
    private ValueTask<SandboxValue> EvaluateUnary(
        UnaryExpression unary,
        InterpreterFrame frame,
        bool allowWidePrimitiveProbe)
    {
        var operand = EvaluateCore(unary.Operand, frame, allowWidePrimitiveProbe);
        return operand.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(OperatorEvaluator.ApplyUnary(unary, operand.Result))
            : AwaitUnary(unary, operand);
    }

    private async ValueTask<SandboxValue> AwaitUnary(UnaryExpression unary, ValueTask<SandboxValue> operand)
        => OperatorEvaluator.ApplyUnary(unary, await operand.ConfigureAwait(false));

    private ValueTask<SandboxValue> EvaluateBinary(
        BinaryExpression binary,
        InterpreterFrame frame,
        bool allowWidePrimitiveProbe)
    {
        if (binary.Operator is "&&" or "||")
        {
            return EvaluateShortCircuit(binary, frame, allowWidePrimitiveProbe);
        }

        var leftTask = EvaluateCore(binary.Left, frame, allowWidePrimitiveProbe);
        if (!leftTask.IsCompletedSuccessfully)
        {
            return AwaitBinary(binary, leftTask, frame, allowWidePrimitiveProbe);
        }

        var rightTask = EvaluateCore(binary.Right, frame, allowWidePrimitiveProbe);
        if (!rightTask.IsCompletedSuccessfully)
        {
            return AwaitBinaryRight(binary, leftTask.Result, rightTask);
        }

        return new ValueTask<SandboxValue>(OperatorEvaluator.ApplyBinary(
            binary,
            leftTask.Result,
            rightTask.Result,
            Context));
    }

    private async ValueTask<SandboxValue> AwaitBinary(
        BinaryExpression binary,
        ValueTask<SandboxValue> leftTask,
        InterpreterFrame frame,
        bool allowWidePrimitiveProbe)
    {
        var left = await leftTask.ConfigureAwait(false);
        var right = await EvaluateCore(
            binary.Right,
            frame,
            allowWidePrimitiveProbe).ConfigureAwait(false);
        return OperatorEvaluator.ApplyBinary(binary, left, right, Context);
    }

    private async ValueTask<SandboxValue> AwaitBinaryRight(
        BinaryExpression binary,
        SandboxValue left,
        ValueTask<SandboxValue> rightTask)
        => OperatorEvaluator.ApplyBinary(
            binary,
            left,
            await rightTask.ConfigureAwait(false),
            Context);

    private ValueTask<SandboxValue> EvaluateShortCircuit(
        BinaryExpression binary,
        InterpreterFrame frame,
        bool allowWidePrimitiveProbe)
    {
        var shortCircuitOn = binary.Operator == "||";
        var order = ShortCircuitExpressionOrder.Choose(
            binary,
            Context.Bindings,
            FunctionAnalysis);
        var firstTask = EvaluateCore(order.First, frame, allowWidePrimitiveProbe);
        if (!firstTask.IsCompletedSuccessfully)
        {
            return AwaitShortCircuit(
                order,
                shortCircuitOn,
                firstTask,
                frame,
                allowWidePrimitiveProbe);
        }

        if (((BoolValue)firstTask.Result).Value == shortCircuitOn)
        {
            return new ValueTask<SandboxValue>(SandboxValue.FromBool(shortCircuitOn));
        }

        var secondTask = EvaluateCore(order.Second, frame, allowWidePrimitiveProbe);
        return secondTask.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(SandboxValue.FromBool(((BoolValue)secondTask.Result).Value))
            : AwaitShortCircuitSecond(secondTask);
    }

    private async ValueTask<SandboxValue> AwaitShortCircuit(
        ShortCircuitOperands order,
        bool shortCircuitOn,
        ValueTask<SandboxValue> firstTask,
        InterpreterFrame frame,
        bool allowWidePrimitiveProbe)
    {
        var first = (BoolValue)await firstTask.ConfigureAwait(false);
        if (first.Value == shortCircuitOn)
        {
            return SandboxValue.FromBool(shortCircuitOn);
        }

        var second = (BoolValue)await EvaluateCore(
            order.Second,
            frame,
            allowWidePrimitiveProbe).ConfigureAwait(false);
        return SandboxValue.FromBool(second.Value);
    }

    private static async ValueTask<SandboxValue> AwaitShortCircuitSecond(ValueTask<SandboxValue> secondTask)
        => SandboxValue.FromBool(((BoolValue)await secondTask.ConfigureAwait(false)).Value);
}
