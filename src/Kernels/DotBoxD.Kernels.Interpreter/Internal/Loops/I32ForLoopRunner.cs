using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class I32ForLoopRunner
{
    private const int CheckpointInterval = 4096;
    private const long LoopFuel = 5;

    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options,
        I32CallEvaluator calls)
    {
        if (options.EnableDebugTrace || start >= end)
        {
            return false;
        }

        if (statement.Body.Count == 1)
        {
            var singleLoopSlot = frame.GetSlot(statement.LocalName);
            if (frame.Layout.LoopPlans.TryGetI32ForRangePlan(
                    statement,
                    frame,
                    singleLoopSlot,
                    out var cached))
            {
                RunValidatedCachedSingleAssignment(
                    start,
                    end,
                    frame,
                    context,
                    singleLoopSlot,
                    cached);
                return true;
            }

            return TryRunSingleAssignment(statement, start, end, frame, context, calls, singleLoopSlot);
        }

        if (!TryCreateBodyPlan(statement, frame, calls, out var body, out var fuelPerIteration))
        {
            return false;
        }

        context.ChargeLoopIterations((long)end - start, fuelPerIteration);
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = CheckpointInterval;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            for (var statementIndex = 0; statementIndex < body.Length; statementIndex++)
            {
                var assignment = body[statementIndex];
                frame.WriteRawInt32Slot(assignment.TargetSlot, assignment.Expression.Evaluate(frame, context));
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = CheckpointInterval;
            }
        }

        return true;
    }

    private static bool TryRunSingleAssignment(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        I32CallEvaluator calls,
        int loopSlot)
    {
        if (!TryCreateAssignmentPlan(
                statement.Body[0],
                frame,
                statement.LocalName,
                calls,
                out var assignment,
                out var assignmentFuel))
        {
            return false;
        }

        var fuelPerIteration = LoopFuel + assignmentFuel;
        if (assignment.Expression.HasOnlyRawVariables())
        {
            ref var loopPlans = ref frame.Layout.LoopPlans;
            if (loopPlans.ShouldCacheI32ForRangePlan(statement))
            {
                loopPlans.CacheI32ForRangePlan(new I32ForLoopPlan(
                    statement,
                    assignment.TargetSlot,
                    assignment.Expression,
                    fuelPerIteration,
                    assignment.Expression.GetRequiredRawSlots()));
            }
        }

        RunSingleAssignment(
            start,
            end,
            frame,
            context,
            loopSlot,
            assignment.TargetSlot,
            assignment.Expression,
            fuelPerIteration);
        return true;
    }

    /// <summary>
    /// Runs a plan that the layout cache has validated against this frame and the caller's
    /// promised slot writes. Callers must obtain it from <see cref="FunctionLoopPlans"/>
    /// and perform those writes before the cached expression is evaluated.
    /// </summary>
    internal static void RunValidatedCachedSingleAssignment(
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        int loopSlot,
        I32ForLoopPlan plan)
        => RunSingleAssignment(
            start,
            end,
            frame,
            context,
            loopSlot,
            plan.TargetSlot,
            plan.Expression,
            plan.FuelPerIteration);

    private static void RunSingleAssignment(
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        int loopSlot,
        int targetSlot,
        I32ExpressionPlan expression,
        long fuelPerIteration)
    {
        context.ChargeLoopIterations((long)end - start, fuelPerIteration);
        var checkpoint = CheckpointInterval;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            frame.WriteRawInt32Slot(
                targetSlot,
                expression.Evaluate(frame, context));

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = CheckpointInterval;
            }
        }
    }

    private static bool TryCreateBodyPlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        I32CallEvaluator calls,
        out AssignmentPlan[] body,
        out long fuelPerIteration)
    {
        body = new AssignmentPlan[statement.Body.Count];
        fuelPerIteration = LoopFuel;
        for (var i = 0; i < statement.Body.Count; i++)
        {
            if (!TryCreateAssignmentPlan(
                    statement.Body[i],
                    frame,
                    statement.LocalName,
                    calls,
                    out body[i],
                    out var assignmentFuel))
            {
                body = [];
                return false;
            }

            fuelPerIteration += assignmentFuel;
        }

        return true;
    }

    private static bool TryCreateAssignmentPlan(
        Statement statement,
        InterpreterFrame frame,
        string loopLocal,
        I32CallEvaluator calls,
        out AssignmentPlan plan,
        out long fuel)
    {
        plan = default;
        fuel = 0;
        if (statement is not AssignmentStatement assignment ||
            !I32ExpressionPlan.TryCreate(assignment.Value, frame, loopLocal, calls, out var expression))
        {
            return false;
        }

        var targetSlot = frame.GetSlot(assignment.Name);
        if (!frame.IsInt32Slot(targetSlot))
        {
            return false;
        }

        plan = new AssignmentPlan(targetSlot, expression);
        fuel = 1 + expression.FuelCost;
        return true;
    }

    private readonly record struct AssignmentPlan(int TargetSlot, I32ExpressionPlan Expression);
}
