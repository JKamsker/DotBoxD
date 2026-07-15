using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal static class LocalFunctionCallEvaluator
{
    public static ValueTask<SandboxValue> Evaluate(
        ExpressionEvaluator evaluator,
        CallExpression call,
        SandboxFunction function,
        InterpreterFrame frame)
    {
        var argumentCount = call.Arguments.Count;
        var firstTask = evaluator.EvaluateAsync(call.Arguments[0], frame);
        if (!firstTask.IsCompletedSuccessfully)
        {
            return AwaitArguments(
                evaluator,
                call,
                function,
                new SandboxValue[argumentCount],
                pending: 0,
                firstTask,
                frame);
        }

        var first = firstTask.Result;
        if (argumentCount == 1)
        {
            return evaluator.InvokeFunctionAsync(function, LocalFunctionArguments.FromSingle(first));
        }

        var secondTask = evaluator.EvaluateAsync(call.Arguments[1], frame);
        if (!secondTask.IsCompletedSuccessfully)
        {
            var arguments = new SandboxValue[argumentCount];
            arguments[0] = first;
            return AwaitArguments(
                evaluator,
                call,
                function,
                arguments,
                pending: 1,
                secondTask,
                frame);
        }

        var second = secondTask.Result;
        if (argumentCount == 2)
        {
            return evaluator.InvokeFunctionAsync(function, LocalFunctionArguments.FromPair(first, second));
        }

        var thirdTask = evaluator.EvaluateAsync(call.Arguments[2], frame);
        if (!thirdTask.IsCompletedSuccessfully)
        {
            var arguments = new SandboxValue[3];
            arguments[0] = first;
            arguments[1] = second;
            return AwaitArguments(
                evaluator,
                call,
                function,
                arguments,
                pending: 2,
                thirdTask,
                frame);
        }

        return evaluator.InvokeFunctionAsync(
            function,
            new LocalFunctionTripleArguments(first, second, thirdTask.Result));
    }

    private static async ValueTask<SandboxValue> AwaitArguments(
        ExpressionEvaluator evaluator,
        CallExpression call,
        SandboxFunction function,
        SandboxValue[] arguments,
        int pending,
        ValueTask<SandboxValue> pendingTask,
        InterpreterFrame frame)
    {
        arguments[pending] = await pendingTask.ConfigureAwait(false);
        for (var i = pending + 1; i < arguments.Length; i++)
        {
            arguments[i] = await evaluator.EvaluateAsync(call.Arguments[i], frame).ConfigureAwait(false);
        }

        return await evaluator.InvokeFunctionAsync(
            function,
            LocalFunctionArguments.FromArray(arguments)).ConfigureAwait(false);
    }
}
