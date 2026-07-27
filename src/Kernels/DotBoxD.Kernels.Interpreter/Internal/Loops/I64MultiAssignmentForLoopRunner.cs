using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Loops;

internal static class I64MultiAssignmentForLoopRunner
{
    private const int CheckpointInterval = 4096;
    private const long LoopFuel = 5;

    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context)
    {
        if (frame.Layout.LoopPlans.TryGetI64ForRangePlan(statement, frame, out var cached) &&
            cached.IsMultiAssignment)
        {
            return TryRun(
                statement,
                cached.MultipleAssignments,
                cached.FuelPerIteration,
                start,
                end,
                frame,
                context);
        }

        if (!TryCreateBody(statement, frame, out var body, out var fuelPerIteration))
        {
            return false;
        }

        ref var loopPlans = ref frame.Layout.LoopPlans;
        if (loopPlans.ShouldCacheI64ForRangePlan(statement))
        {
            loopPlans.CacheI64ForRangePlan(new I64ForLoopPlan(
                statement,
                body,
                fuelPerIteration));
        }

        return TryRun(statement, body, fuelPerIteration, start, end, frame, context);
    }

    private static bool TryRun(
        ForRangeStatement statement,
        ReadOnlySpan<I64LoopAssignmentPlan> body,
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
            for (var statementIndex = 0; statementIndex < body.Length; statementIndex++)
            {
                var assignment = body[statementIndex];
                frame.WriteRawInt64Slot(
                    assignment.TargetSlot,
                    assignment.Expression.Evaluate(frame));
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = CheckpointInterval;
            }
        }

        return true;
    }

    private static bool TryCreateBody(
        ForRangeStatement statement,
        InterpreterFrame frame,
        out I64LoopAssignmentPlan[] body,
        out long fuelPerIteration)
    {
        body = [];
        fuelPerIteration = LoopFuel;
        if (statement.Body.Count < 2)
        {
            return false;
        }

        var plans = new I64LoopAssignmentPlan[statement.Body.Count];
        var assignedSlots = new HashSet<int>();
        var slotReads = new I64ExpressionSlotReadState(frame, assignedSlots);
        var fuel = LoopFuel;
        for (var i = 0; i < statement.Body.Count; i++)
        {
            if (statement.Body[i] is not AssignmentStatement assignment ||
                !I64ExpressionPlan.TryCreate(assignment.Value, in slotReads, out var expression))
            {
                return false;
            }

            var targetSlot = frame.GetSlot(assignment.Name);
            if (!frame.IsI64Slot(targetSlot))
            {
                return false;
            }

            plans[i] = new I64LoopAssignmentPlan(targetSlot, expression);
            assignedSlots.Add(targetSlot);
            fuel += 1 + expression.FuelCost;
        }

        body = plans;
        fuelPerIteration = fuel;
        return true;
    }
}
