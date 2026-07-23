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
        var targeted = TryExecuteTargetedAssignment(
            assignment, frame, expressions, out var targetSlot);
        if (targeted == TargetedAssignmentResult.Executed)
        {
            return default;
        }

        // Eligibility probes are pure and unmetered. Reuse a known raw target's
        // failed probe while retaining the legacy evaluator order for every
        // remaining kind, then the generic fallback.
        if (targeted != TargetedAssignmentResult.I32Miss &&
            expressions.TryEvaluateInt32(assignment.Value, frame, out var i32Value))
        {
            WriteInt32(frame, assignment.Name, targetSlot, i32Value);
            return default;
        }

        if (targeted != TargetedAssignmentResult.I64Miss &&
            expressions.TryEvaluateInt64(assignment.Value, frame, out var i64Value))
        {
            WriteInt64(frame, assignment.Name, targetSlot, i64Value);
            return default;
        }

        if (targeted != TargetedAssignmentResult.F64Miss &&
            expressions.TryEvaluateDouble(assignment.Value, frame, out var f64Value))
        {
            WriteDouble(frame, assignment.Name, targetSlot, f64Value);
            return default;
        }

        var valueTask = expressions.EvaluateAsync(assignment.Value, frame);
        if (valueTask.IsCompletedSuccessfully)
        {
            WriteValue(frame, assignment.Name, targetSlot, valueTask.Result);
            return default;
        }

        return AwaitAssignment(assignment, targetSlot, valueTask, frame);
    }

    private static TargetedAssignmentResult TryExecuteTargetedAssignment(
        AssignmentStatement assignment,
        InterpreterFrame frame,
        ExpressionEvaluator expressions,
        out int targetSlot)
    {
        if (!frame.TryGetSlot(assignment.Name, out targetSlot))
        {
            targetSlot = -1;
            return TargetedAssignmentResult.NotApplicable;
        }

        if (frame.IsInt32Slot(targetSlot))
        {
            if (!expressions.TryEvaluateInt32(assignment.Value, frame, out var value))
            {
                return TargetedAssignmentResult.I32Miss;
            }

            frame.WriteRawInt32Slot(targetSlot, value);
            return TargetedAssignmentResult.Executed;
        }

        if (frame.IsI64Slot(targetSlot))
        {
            if (!expressions.TryEvaluateInt64(assignment.Value, frame, out var value))
            {
                return TargetedAssignmentResult.I64Miss;
            }

            frame.WriteRawInt64Slot(targetSlot, value);
            return TargetedAssignmentResult.Executed;
        }

        if (frame.IsF64Slot(targetSlot))
        {
            if (!expressions.TryEvaluateDouble(assignment.Value, frame, out var value))
            {
                return TargetedAssignmentResult.F64Miss;
            }

            frame.WriteRawDoubleSlot(targetSlot, value);
            return TargetedAssignmentResult.Executed;
        }

        return TargetedAssignmentResult.NotApplicable;
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

    private static void WriteInt32(InterpreterFrame frame, string name, int targetSlot, int value)
    {
        if (targetSlot >= 0)
        {
            frame.WriteSlot(targetSlot, SandboxValue.FromInt32(value));
            return;
        }

        frame.WriteInt32(name, value);
    }

    private static void WriteInt64(InterpreterFrame frame, string name, int targetSlot, long value)
    {
        if (targetSlot >= 0)
        {
            frame.WriteSlot(targetSlot, SandboxValue.FromInt64(value));
            return;
        }

        var slot = frame.GetSlot(name);
        if (frame.IsI64Slot(slot))
        {
            frame.WriteRawInt64Slot(slot, value);
            return;
        }

        frame.Write(name, SandboxValue.FromInt64(value));
    }

    private static void WriteDouble(InterpreterFrame frame, string name, int targetSlot, double value)
    {
        if (targetSlot >= 0)
        {
            frame.WriteSlot(targetSlot, SandboxValue.FromDouble(value));
            return;
        }

        var slot = frame.GetSlot(name);
        if (frame.IsF64Slot(slot))
        {
            frame.WriteRawDoubleSlot(slot, value);
            return;
        }

        frame.Write(name, SandboxValue.FromDouble(value));
    }

    private static void WriteValue(
        InterpreterFrame frame,
        string name,
        int targetSlot,
        SandboxValue value)
    {
        if (targetSlot >= 0)
        {
            frame.WriteSlot(targetSlot, value);
            return;
        }

        frame.Write(name, value);
    }

    private static async ValueTask<SandboxValue?> AwaitAssignment(
        AssignmentStatement assignment,
        int targetSlot,
        ValueTask<SandboxValue> valueTask,
        InterpreterFrame frame)
    {
        WriteValue(
            frame,
            assignment.Name,
            targetSlot,
            await valueTask.ConfigureAwait(false));
        return null;
    }

    private static async ValueTask<SandboxValue?> AwaitReturnAsync(ValueTask<SandboxValue> valueTask)
        => await valueTask.ConfigureAwait(false);

    private enum TargetedAssignmentResult
    {
        NotApplicable,
        Executed,
        I32Miss,
        I64Miss,
        F64Miss
    }
}
