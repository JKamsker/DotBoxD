using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Loops;

// Fast path for `forRange { if (<i32 comparison>) { <f64 assigns> } else { <f64 assigns> } }` — an i32 loop
// variable with an f64 (binding-free) branched body. Evaluates the condition and both branches with unboxed
// i32/f64 plans, avoiding the boxed statement executor's per-op allocation. Metering matches the general path:
// per iteration 5 (loop) + 1 + condition-node-fuel (if), and each taken assignment 1 + f64 expression node fuel.
internal static class BranchedF64ForLoopRunner
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
        if (options.EnableDebugTrace)
        {
            return false;
        }

        if (!TryCreateLoopPlan(statement, start, end, frame, context, calls, out var plan))
        {
            return false;
        }

        var loopSlot = frame.GetSlot(statement.LocalName);
        long conditionFuel = 1 + plan.Condition.FuelCost;
        var iterations = (long)end - start;
        var fuelPerIteration = LoopFuel + conditionFuel + plan.Then.Fuel;
        if (plan.Then.Fuel == plan.Else.Fuel &&
            context.CanBulkChargeLoopIterations(iterations, fuelPerIteration))
        {
            context.ChargeLoopIterations(iterations, fuelPerIteration);
            RunBulkMetered(plan, start, end, loopSlot, frame, context);
            return true;
        }

        var checkpoint = CheckpointInterval;
        for (var i = start; i < end; i++)
        {
            context.ChargeLoopIteration(LoopFuel);
            context.ChargeFuel(conditionFuel);

            frame.WriteRawInt32Slot(loopSlot, i);
            var taken = plan.Condition.Evaluate(frame, context) ? plan.Then : plan.Else;
            context.ChargeFuel(taken.Fuel);

            switch (taken.Kind)
            {
                case BranchKind.Empty:
                    break;
                case BranchKind.Single:
                {
                    var assignment = taken.SingleAssignment;
                    frame.WriteRawDoubleSlot(
                        assignment.TargetSlot,
                        assignment.Expression.Evaluate(frame));
                    break;
                }
                case BranchKind.Many:
                {
                    var assignments = taken.MultipleAssignments!;
                    for (var statementIndex = 0; statementIndex < assignments.Length; statementIndex++)
                    {
                        var assignment = assignments[statementIndex];
                        frame.WriteRawDoubleSlot(
                            assignment.TargetSlot,
                            assignment.Expression.Evaluate(frame));
                    }

                    break;
                }
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = CheckpointInterval;
            }
        }

        return true;
    }

    private static void RunBulkMetered(
        in BranchedLoopPlan plan,
        int start,
        int end,
        int loopSlot,
        InterpreterFrame frame,
        SandboxContext context)
    {
        var checkpoint = CheckpointInterval;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            var taken = plan.Condition.Evaluate(frame, context) ? plan.Then : plan.Else;
            switch (taken.Kind)
            {
                case BranchKind.Empty:
                    break;
                case BranchKind.Single:
                {
                    var assignment = taken.SingleAssignment;
                    frame.WriteRawDoubleSlot(
                        assignment.TargetSlot,
                        assignment.Expression.Evaluate(frame));
                    break;
                }
                case BranchKind.Many:
                {
                    var assignments = taken.MultipleAssignments!;
                    for (var statementIndex = 0; statementIndex < assignments.Length; statementIndex++)
                    {
                        var assignment = assignments[statementIndex];
                        frame.WriteRawDoubleSlot(
                            assignment.TargetSlot,
                            assignment.Expression.Evaluate(frame));
                    }

                    break;
                }
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = CheckpointInterval;
            }
        }
    }

    private static bool TryCreateLoopPlan(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        I32CallEvaluator calls,
        out BranchedLoopPlan plan)
    {
        plan = default;
        if (start >= end)
        {
            return false;
        }

        if (statement.Body.Count != 1 || statement.Body[0] is not IfStatement branch)
        {
            return false;
        }

        if (!I32ComparisonPlan.TryCreate(branch.Condition, frame, statement.LocalName, calls, out var condition))
        {
            return false;
        }

        if (!TryCreateBranch(branch.Then, frame, context.Bindings, out var thenBranch))
        {
            return false;
        }

        if (!TryCreateBranch(branch.Else, frame, context.Bindings, out var elseBranch))
        {
            return false;
        }

        plan = new BranchedLoopPlan(condition, thenBranch, elseBranch);
        return true;
    }

    private static bool TryCreateBranch(
        IReadOnlyList<Statement> statements,
        InterpreterFrame frame,
        IBindingCatalog bindings,
        out Branch branch)
    {
        branch = default;
        if (statements.Count == 0)
        {
            branch = new Branch(BranchKind.Empty, default, null, 0);
            return true;
        }

        if (statements.Count == 1)
        {
            if (!TryCreateAssignmentPlan(
                    statements[0],
                    frame,
                    bindings,
                    out var assignment,
                    out var singleFuel))
            {
                return false;
            }

            branch = new Branch(BranchKind.Single, assignment, null, singleFuel);
            return true;
        }

        var assignments = new AssignmentPlan[statements.Count];
        long fuel = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (!TryCreateAssignmentPlan(
                    statements[i],
                    frame,
                    bindings,
                    out assignments[i],
                    out var assignmentFuel))
            {
                return false;
            }

            fuel += assignmentFuel;
        }

        branch = new Branch(BranchKind.Many, default, assignments, fuel);
        return true;
    }

    private static bool TryCreateAssignmentPlan(
        Statement statement,
        InterpreterFrame frame,
        IBindingCatalog bindings,
        out AssignmentPlan plan,
        out long fuel)
    {
        plan = default;
        fuel = 0;
        if (statement is not AssignmentStatement assignment ||
            !F64ExpressionPlan.TryCreate(
                assignment.Value,
                frame,
                assignment.Name,
                bindings,
                out var expression,
                out var binding) ||
            binding is not null)
        {
            return false;
        }

        var targetSlot = frame.GetSlot(assignment.Name);
        if (!frame.IsF64Slot(targetSlot))
        {
            return false;
        }

        plan = new AssignmentPlan(targetSlot, expression);
        fuel = 1 + expression.FuelCost;
        return true;
    }

    private readonly record struct AssignmentPlan(int TargetSlot, F64ExpressionPlan Expression);

    private readonly record struct Branch(
        BranchKind Kind,
        AssignmentPlan SingleAssignment,
        AssignmentPlan[]? MultipleAssignments,
        long Fuel);

    private enum BranchKind
    {
        Empty,
        Single,
        Many
    }

    private readonly record struct BranchedLoopPlan(
        I32ComparisonPlan Condition,
        Branch Then,
        Branch Else);
}
