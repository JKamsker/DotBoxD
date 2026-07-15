using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
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
        if (!CanUseFastPath(options, start, end) ||
            !TryCreatePlan(statement, frame, calls, out var plan))
        {
            return false;
        }

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
                case BranchKind.Empty:
                    break;
                case BranchKind.Single:
                {
                    var assignment = taken.SingleAssignment;
                    frame.WriteRawInt32Slot(
                        assignment.TargetSlot,
                        assignment.Expression.Evaluate(frame, context));
                    break;
                }
                case BranchKind.Many:
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

    private static bool CanUseFastPath(
        SandboxExecutionOptions options,
        int start,
        int end)
        => !options.RequiresInterpreter && start < end;

    private static bool TryCreatePlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        I32CallEvaluator calls,
        out BranchPlan plan)
    {
        plan = default;
        if (statement.Body.Count != 1 ||
            statement.Body[0] is not IfStatement branch ||
            !I32ComparisonPlan.TryCreate(branch.Condition, frame, statement.LocalName, calls, out var condition) ||
            !TryCreateBranch(branch.Then, frame, statement.LocalName, calls, out var thenBranch) ||
            !TryCreateBranch(branch.Else, frame, statement.LocalName, calls, out var elseBranch))
        {
            return false;
        }

        plan = new BranchPlan(condition, thenBranch, elseBranch);
        return true;
    }

    private static bool TryCreateBranch(
        IReadOnlyList<Statement> statements,
        InterpreterFrame frame,
        string loopLocal,
        I32CallEvaluator calls,
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
                    loopLocal,
                    calls,
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
                    loopLocal,
                    calls,
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

    private readonly record struct BranchPlan(I32ComparisonPlan Condition, Branch Then, Branch Else);
}
