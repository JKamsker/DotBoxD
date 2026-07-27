using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal static class InterpreterFunctionExecutor
{
    public static ValueTask<SandboxValue> Invoke(
        InterpreterEvaluator evaluator,
        SandboxFunction function,
        LocalFunctionArguments arguments,
        SandboxValue? entrypointInput)
    {
        var debug = evaluator.DebugFunctions;
        if (debug is not null)
        {
            return debug.InvokeAsync(function, arguments, entrypointInput);
        }

        var context = evaluator.Context;
        context.EnterCall();
        var exited = false;
        try
        {
            context.ChargeFuel(1);
            var layout = evaluator.GetFrameLayout(function);
            var frame = entrypointInput is null
                ? InterpreterFrame.Create(layout, function, arguments)
                : InterpreterFrame.CreateValidatedEntrypoint(layout, function, entrypointInput);
            var body = function.Body;
            for (var i = 0; i < body.Count; i++)
            {
                var statementTask = evaluator.Statements.ExecuteStatementAsync(body[i], frame);
                if (!statementTask.IsCompletedSuccessfully)
                {
                    exited = true;
                    return AwaitInvoke(evaluator, function, statementTask, frame, i + 1);
                }

                var result = statementTask.Result;
                if (result is not null)
                {
                    EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                    return new ValueTask<SandboxValue>(result);
                }
            }

            throw MissingReturn(function);
        }
        finally
        {
            if (!exited)
            {
                context.ExitCall();
            }
        }
    }

    public static ValueTask<SandboxValue> Invoke(
        InterpreterEvaluator evaluator,
        SandboxFunction function,
        LocalFunctionTripleArguments arguments)
    {
        var debug = evaluator.DebugFunctions;
        if (debug is not null)
        {
            return debug.InvokeAsync(
                function,
                LocalFunctionArguments.FromArray([arguments[0], arguments[1], arguments[2]]),
                entrypointInput: null);
        }

        return InvokeTriple(evaluator, function, arguments);
    }

    private static ValueTask<SandboxValue> InvokeTriple(
        InterpreterEvaluator evaluator,
        SandboxFunction function,
        LocalFunctionTripleArguments arguments)
    {
        var context = evaluator.Context;
        context.EnterCall();
        var exited = false;
        try
        {
            context.ChargeFuel(1);
            var frame = InterpreterFrameBuilder.Create(
                evaluator.GetFrameLayout(function),
                function,
                arguments);
            var body = function.Body;
            for (var i = 0; i < body.Count; i++)
            {
                var statementTask = evaluator.Statements.ExecuteStatementAsync(body[i], frame);
                if (!statementTask.IsCompletedSuccessfully)
                {
                    exited = true;
                    return AwaitInvoke(evaluator, function, statementTask, frame, i + 1);
                }

                var result = statementTask.Result;
                if (result is not null)
                {
                    EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                    return new ValueTask<SandboxValue>(result);
                }
            }

            throw MissingReturn(function);
        }
        finally
        {
            if (!exited)
            {
                context.ExitCall();
            }
        }
    }

    private static async ValueTask<SandboxValue> AwaitInvoke(
        InterpreterEvaluator evaluator,
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
                result = await evaluator.Statements.ExecuteStatementAsync(body[i], frame).ConfigureAwait(false);
                if (result is not null)
                {
                    EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                    return result;
                }
            }

            throw MissingReturn(function);
        }
        finally
        {
            evaluator.Context.ExitCall();
        }
    }

    private static SandboxRuntimeException MissingReturn(SandboxFunction function)
        => new(new SandboxError(
            SandboxErrorCode.ValidationError,
            $"function '{function.Id}' returned no value"));
}
