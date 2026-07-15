using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal static class PrimitiveStatementExecutor
{
    public static ValueTask<SandboxValue?> ExecuteAssignment(
        AssignmentStatement assignment,
        InterpreterFrame frame,
        ExpressionEvaluator expressions)
    {
        if (expressions.TryEvaluateInt64(assignment.Value, frame, out var i64Value))
        {
            WriteInt64(frame, assignment.Name, i64Value);
            return default;
        }

        if (expressions.TryEvaluateDouble(assignment.Value, frame, out var f64Value))
        {
            WriteDouble(frame, assignment.Name, f64Value);
            return default;
        }

        var valueTask = expressions.EvaluateAsync(assignment.Value, frame);
        if (valueTask.IsCompletedSuccessfully)
        {
            frame.Write(assignment.Name, valueTask.Result);
            return default;
        }

        return AwaitAssignment(assignment, valueTask, frame);
    }

    public static ValueTask<SandboxValue?> ExecuteReturn(
        ReturnStatement statement,
        InterpreterFrame frame,
        ExpressionEvaluator expressions)
    {
        if (IsPrimitiveReturnCandidate(statement.Value))
        {
            if (expressions.TryEvaluateInt64(statement.Value, frame, out var i64Value))
            {
                return new ValueTask<SandboxValue?>(SandboxValue.FromInt64(i64Value));
            }

            if (expressions.TryEvaluateDouble(statement.Value, frame, out var f64Value))
            {
                return new ValueTask<SandboxValue?>(SandboxValue.FromDouble(f64Value));
            }
        }

        var valueTask = expressions.EvaluateAsync(statement.Value, frame);
        return valueTask.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue?>(valueTask.Result)
            : AwaitReturnAsync(valueTask);
    }

    private static bool IsPrimitiveReturnCandidate(Expression expression)
        // Literal nodes already return their prepared value identity, while a plain
        // raw variable needs only the unavoidable public result box. Only non-leaf
        // arithmetic trees can remove intermediate boxes without regressing either.
        => expression is UnaryExpression or BinaryExpression;

    private static void WriteInt64(InterpreterFrame frame, string name, long value)
    {
        var slot = frame.GetSlot(name);
        if (frame.IsI64Slot(slot))
        {
            frame.WriteRawInt64Slot(slot, value);
            return;
        }

        frame.Write(name, SandboxValue.FromInt64(value));
    }

    private static void WriteDouble(InterpreterFrame frame, string name, double value)
    {
        var slot = frame.GetSlot(name);
        if (frame.IsF64Slot(slot))
        {
            frame.WriteRawDoubleSlot(slot, value);
            return;
        }

        frame.Write(name, SandboxValue.FromDouble(value));
    }

    private static async ValueTask<SandboxValue?> AwaitAssignment(
        AssignmentStatement assignment,
        ValueTask<SandboxValue> valueTask,
        InterpreterFrame frame)
    {
        frame.Write(assignment.Name, await valueTask.ConfigureAwait(false));
        return null;
    }

    private static async ValueTask<SandboxValue?> AwaitReturnAsync(ValueTask<SandboxValue> valueTask)
        => await valueTask.ConfigureAwait(false);
}
