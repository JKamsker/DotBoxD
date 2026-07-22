using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Loops;

// Fast path for `forRange { if (<i32 comparison>) { <i32 assignments> } else { <i32 assignments> } }`.
// Evaluates the condition and both branches with unboxed i32 plans, avoiding the per-op boxing the general
// statement executor would do for a branched loop body.
//
// Metering matches the general compiled/interpreted path exactly: per iteration the loop charges 5
// (ChargeLoopIteration), the if-statement charges 1 plus the condition's node fuel, and each taken assignment
// charges 1 (the set statement) plus its expression's node fuel.
internal static class BranchedI32ForLoopRunner
{
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
        if (!TryPrepareFreshPlan(
                statement,
                start,
                end,
                frame,
                context,
                options,
                calls,
                out var plan,
                out var handled))
        {
            return handled;
        }

        // Keep freshly planned branches in this caller. The JIT does not inline the shared cached-plan
        // runner for multi-assignment plans, and paying that call once per short loop is measurable.
        var loopSlot = frame.GetSlot(statement.LocalName);
        var conditionFuel = 1 + plan.Condition.FuelCost;
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            context.ChargeLoopIteration(LoopFuel);
            context.ChargeFuel(conditionFuel);
            frame.WriteRawInt32Slot(loopSlot, i);
            var taken = plan.Condition.Evaluate(frame, context) ? plan.Then : plan.Else;
            context.ChargeFuel(taken.Fuel);
            switch (taken.Kind)
            {
                case I32BranchedBranchKind.Empty:
                    break;
                case I32BranchedBranchKind.Single:
                {
                    var assignment = taken.SingleAssignment;
                    frame.WriteRawInt32Slot(
                        assignment.TargetSlot,
                        assignment.Expression.Evaluate(frame, context));
                    break;
                }
                case I32BranchedBranchKind.Many:
                {
                    var assignments = taken.MultipleAssignments!;
                    for (var statementIndex = 0; statementIndex < assignments.Length; statementIndex++)
                    {
                        var assignment = assignments[statementIndex];
                        frame.WriteRawInt32Slot(
                            assignment.TargetSlot,
                            assignment.Expression.Evaluate(frame, context));
                    }

                    break;
                }
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = 4096;
            }
        }

        return true;
    }

    private static bool TryPrepareFreshPlan(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options,
        I32CallEvaluator calls,
        out I32BranchedPlanData plan,
        out bool handled)
    {
        plan = default;
        handled = false;
        if (!CanUseFastPath(options, start, end) ||
            statement.Body is not [IfStatement branch])
        {
            return false;
        }

        if (branch.Then.Count <= 1 && branch.Else.Count <= 1)
        {
            return TryPrepareReusablePlan(
                statement,
                branch,
                start,
                end,
                frame,
                context,
                calls,
                out plan,
                out handled);
        }

        return I32BranchedLoopPlanner.TryCreate(
            branch,
            statement.LocalName,
            frame,
            calls,
            out plan);
    }

    private static bool TryPrepareReusablePlan(
        ForRangeStatement statement,
        IfStatement branch,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        I32CallEvaluator calls,
        out I32BranchedPlanData plan,
        out bool handled)
    {
        plan = default;
        handled = false;
        if (!I32BranchedLoopPlanner.TryGetKnownTarget(branch, frame, out var knownTarget))
        {
            return false;
        }

        ref var loopPlans = ref frame.Layout.LoopPlans;
        if (loopPlans.TryGetI32BranchedForRangePlan(statement, frame, out var cached))
        {
            Run(cached.Data, start, end, frame.GetSlot(statement.LocalName), frame, context);
            handled = true;
            return false;
        }

        if (!I32BranchedLoopPlanner.TryCreateReusable(
                branch,
                statement.LocalName,
                frame,
                calls,
                knownTarget,
                out plan))
        {
            return false;
        }

        if (plan.IsReusable && loopPlans.ShouldCacheI32BranchedForRangePlan(statement))
        {
            loopPlans.CacheI32BranchedForRangePlan(new I32BranchedLoopPlan(statement, plan));
        }

        return true;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void Run(
        in I32BranchedPlanData plan,
        int start,
        int end,
        int loopSlot,
        InterpreterFrame frame,
        SandboxContext context)
    {
        var conditionFuel = 1 + plan.Condition.FuelCost;
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            context.ChargeLoopIteration(LoopFuel);
            context.ChargeFuel(conditionFuel);
            frame.WriteRawInt32Slot(loopSlot, i);
            var taken = plan.Condition.Evaluate(frame, context) ? plan.Then : plan.Else;
            context.ChargeFuel(taken.Fuel);
            switch (taken.Kind)
            {
                case I32BranchedBranchKind.Empty:
                    break;
                case I32BranchedBranchKind.Single:
                {
                    var assignment = taken.SingleAssignment;
                    frame.WriteRawInt32Slot(
                        assignment.TargetSlot,
                        assignment.Expression.Evaluate(frame, context));
                    break;
                }
                case I32BranchedBranchKind.Many:
                {
                    var assignments = taken.MultipleAssignments!;
                    for (var statementIndex = 0; statementIndex < assignments.Length; statementIndex++)
                    {
                        var assignment = assignments[statementIndex];
                        frame.WriteRawInt32Slot(
                            assignment.TargetSlot,
                            assignment.Expression.Evaluate(frame, context));
                    }

                    break;
                }
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = 4096;
            }
        }
    }

    private static bool CanUseFastPath(
        SandboxExecutionOptions options,
        int start,
        int end)
        => !options.EnableDebugTrace && start < end;
}
