using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Interpreter.Debugging;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal static class InterpreterExpressionInstrumentation
{
    public static async ValueTask<SandboxValue> EvaluateDebugAsync(
        ExpressionEvaluator evaluator,
        InterpreterDebugState debug,
        Expression expression,
        InterpreterFrame frame)
    {
        var previousNode = debug.EnterNode(expression);
        try
        {
            await debug.CheckpointAsync(
                    SandboxDebugCheckpointKind.Expression,
                    expression,
                    frame)
                .ConfigureAwait(false);
            if (expression is CallExpression)
            {
                await debug.CheckpointAsync(
                        SandboxDebugCheckpointKind.Call,
                        expression,
                        frame)
                    .ConfigureAwait(false);
            }

            evaluator.Context.ChargeFuel(1);
            return await evaluator.EvaluateNode(
                    expression,
                    frame,
                    allowDescendantProbe: false)
                .ConfigureAwait(false);
        }
        catch (SandboxRuntimeException exception)
        {
            await debug.ReportExceptionAsync(exception).ConfigureAwait(false);
            throw;
        }
        finally
        {
            debug.RestoreNode(previousNode);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteTrace(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        Expression expression,
        InterpreterFrame frame)
    {
        if (options.EnableDebugTrace)
        {
            InterpreterTrace.Write(
                context,
                options,
                moduleHash,
                frame.FunctionId,
                "expression",
                expression.GetType().Name,
                expression.Span);
        }
    }
}
