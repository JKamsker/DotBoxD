using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Loops;

// Fast path for `forRange { <i64 assigns> }` (i32 loop var, i64 assignment targets). Evaluates the body with
// unboxed i64 plans, avoiding the boxed evaluator's per-op I64Value allocation. Bulk-charges loop fuel per
// iteration, matching the i32/f64 runners (loop base 5 + per assignment 1 + expression node fuel).
internal static class I64ForLoopRunner
{
    private const int CheckpointInterval = 4096;
    private const long LoopFuel = 5;

    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options)
    {
        if (options.EnableDebugTrace || start >= end)
        {
            return false;
        }

        return statement.Body.Count == 1
            ? TryRunSingleAssignment(statement, start, end, frame, context)
            : I64MultiAssignmentForLoopRunner.TryRun(
                statement,
                start,
                end,
                frame,
                context);
    }

    private static bool TryRunSingleAssignment(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context)
    {
        if (frame.Layout.LoopPlans.TryGetI64ForRangePlan(statement, frame, out var cached) &&
            cached.IsSingleAssignment)
        {
            return TryRunSingleAssignment(cached, start, end, frame, context);
        }

        if (!TryCreateSingleAssignmentPlan(
                statement.Body[0],
                frame,
                out var assignment,
                out var fuelPerIteration))
        {
            return false;
        }

        ref var loopPlans = ref frame.Layout.LoopPlans;
        if (loopPlans.ShouldCacheI64ForRangePlan(statement))
        {
            loopPlans.CacheI64ForRangePlan(new I64ForLoopPlan(
                statement,
                assignment.TargetSlot,
                assignment.Expression,
                fuelPerIteration));
        }

        return TryRunSingleAssignment(
            statement,
            assignment.TargetSlot,
            assignment.Expression,
            fuelPerIteration,
            start,
            end,
            frame,
            context);
    }

    private static bool TryRunSingleAssignment(
        I64ForLoopPlan plan,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context)
        => TryRunSingleAssignment(
            plan.Statement,
            plan.TargetSlot,
            plan.Expression,
            plan.FuelPerIteration,
            start,
            end,
            frame,
            context);

    private static bool TryRunSingleAssignment(
        ForRangeStatement statement,
        int targetSlot,
        I64ExpressionPlan expression,
        long fuelPerIteration,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context)
    {
        var iterations = (long)end - start;
        if (!context.CanBulkChargeLoopIterations(iterations, fuelPerIteration))
        {
            return false;
        }

        context.ChargeLoopIterations(iterations, fuelPerIteration);
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = CheckpointInterval;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            frame.WriteRawInt64Slot(
                targetSlot,
                expression.Evaluate(frame));

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = CheckpointInterval;
            }
        }

        return true;
    }

    private static bool TryCreateSingleAssignmentPlan(
        Statement statement,
        InterpreterFrame frame,
        out AssignmentPlan plan,
        out long fuelPerIteration)
    {
        plan = default;
        fuelPerIteration = LoopFuel;
        if (statement is not AssignmentStatement assignment ||
            !I64ExpressionPlan.TryCreate(assignment.Value, frame, out var expression))
        {
            return false;
        }

        var targetSlot = frame.GetSlot(assignment.Name);
        if (!frame.IsI64Slot(targetSlot))
        {
            return false;
        }

        plan = new AssignmentPlan(targetSlot, expression);
        fuelPerIteration += 1 + expression.FuelCost;
        return true;
    }

    private readonly record struct AssignmentPlan(int TargetSlot, I64ExpressionPlan Expression);
}
