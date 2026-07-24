using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal static class BindingCallEvaluator
{
    public static ValueTask<SandboxValue> Evaluate(
        ExpressionEvaluator evaluator,
        CallExpression call,
        BindingDescriptor descriptor,
        InterpreterFrame frame)
    {
        if (call.Arguments.Count == 3)
        {
            return EvaluateTriple(evaluator, call, descriptor, frame);
        }

        var firstTask = evaluator.EvaluateAsync(call.Arguments[0], frame);
        if (!firstTask.IsCompletedSuccessfully)
        {
            return AwaitFirstAsync(evaluator, call, descriptor, firstTask, frame);
        }

        var first = firstTask.Result;
        if (call.Arguments.Count == 1)
        {
            return evaluator.InvokeBindingAsync(descriptor, first, frame.FunctionId);
        }

        var secondTask = evaluator.EvaluateAsync(call.Arguments[1], frame);
        return secondTask.IsCompletedSuccessfully
            ? evaluator.InvokeBindingAsync(descriptor, first, secondTask.Result, frame.FunctionId)
            : AwaitSecondAsync(evaluator, descriptor, first, secondTask, frame.FunctionId);
    }

    private static async ValueTask<SandboxValue> AwaitFirstAsync(
        ExpressionEvaluator evaluator,
        CallExpression call,
        BindingDescriptor descriptor,
        ValueTask<SandboxValue> firstTask,
        InterpreterFrame frame)
    {
        var first = await firstTask.ConfigureAwait(false);
        if (call.Arguments.Count == 1)
        {
            return await evaluator.InvokeBindingAsync(
                descriptor, first, frame.FunctionId).ConfigureAwait(false);
        }

        var second = await evaluator.EvaluateAsync(call.Arguments[1], frame).ConfigureAwait(false);
        return await evaluator.InvokeBindingAsync(
            descriptor, first, second, frame.FunctionId).ConfigureAwait(false);
    }

    private static async ValueTask<SandboxValue> AwaitSecondAsync(
        ExpressionEvaluator evaluator,
        BindingDescriptor descriptor,
        SandboxValue first,
        ValueTask<SandboxValue> secondTask,
        string functionId)
    {
        var second = await secondTask.ConfigureAwait(false);
        return await evaluator.InvokeBindingAsync(
            descriptor, first, second, functionId).ConfigureAwait(false);
    }

    private static ValueTask<SandboxValue> EvaluateTriple(
        ExpressionEvaluator evaluator,
        CallExpression call,
        BindingDescriptor descriptor,
        InterpreterFrame frame)
    {
        var firstTask = evaluator.EvaluateAsync(call.Arguments[0], frame);
        if (!firstTask.IsCompletedSuccessfully)
        {
            return AwaitTripleFirstAsync(evaluator, call, descriptor, firstTask, frame);
        }

        var first = firstTask.Result;
        var secondTask = evaluator.EvaluateAsync(call.Arguments[1], frame);
        if (!secondTask.IsCompletedSuccessfully)
        {
            return AwaitTripleSecondAsync(evaluator, call, descriptor, first, secondTask, frame);
        }

        var second = secondTask.Result;
        var thirdTask = evaluator.EvaluateAsync(call.Arguments[2], frame);
        return thirdTask.IsCompletedSuccessfully
            ? evaluator.InvokeBindingAsync(
                descriptor, first, second, thirdTask.Result, frame.FunctionId)
            : AwaitTripleThirdAsync(
                evaluator, descriptor, first, second, thirdTask, frame.FunctionId);
    }

    private static async ValueTask<SandboxValue> AwaitTripleFirstAsync(
        ExpressionEvaluator evaluator,
        CallExpression call,
        BindingDescriptor descriptor,
        ValueTask<SandboxValue> firstTask,
        InterpreterFrame frame)
    {
        var first = await firstTask.ConfigureAwait(false);
        var second = await evaluator.EvaluateAsync(call.Arguments[1], frame).ConfigureAwait(false);
        var third = await evaluator.EvaluateAsync(call.Arguments[2], frame).ConfigureAwait(false);
        return await evaluator.InvokeBindingAsync(
            descriptor, first, second, third, frame.FunctionId).ConfigureAwait(false);
    }

    private static async ValueTask<SandboxValue> AwaitTripleSecondAsync(
        ExpressionEvaluator evaluator,
        CallExpression call,
        BindingDescriptor descriptor,
        SandboxValue first,
        ValueTask<SandboxValue> secondTask,
        InterpreterFrame frame)
    {
        var second = await secondTask.ConfigureAwait(false);
        var third = await evaluator.EvaluateAsync(call.Arguments[2], frame).ConfigureAwait(false);
        return await evaluator.InvokeBindingAsync(
            descriptor, first, second, third, frame.FunctionId).ConfigureAwait(false);
    }

    private static async ValueTask<SandboxValue> AwaitTripleThirdAsync(
        ExpressionEvaluator evaluator,
        BindingDescriptor descriptor,
        SandboxValue first,
        SandboxValue second,
        ValueTask<SandboxValue> thirdTask,
        string functionId)
    {
        var third = await thirdTask.ConfigureAwait(false);
        return await evaluator.InvokeBindingAsync(
            descriptor, first, second, third, functionId).ConfigureAwait(false);
    }
}
