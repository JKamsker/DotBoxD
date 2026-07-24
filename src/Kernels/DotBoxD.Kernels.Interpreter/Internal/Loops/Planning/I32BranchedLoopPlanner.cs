using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;

namespace DotBoxD.Kernels.Interpreter.Internal.Loops;

internal static class I32BranchedLoopPlanner
{
    public static bool TryGetKnownTarget(
        IfStatement branch,
        InterpreterFrame frame,
        out KnownTarget target)
    {
        var assignment = branch.Then.Count > 0
            ? branch.Then[0] as AssignmentStatement
            : branch.Else.Count > 0 ? branch.Else[0] as AssignmentStatement : null;
        if (assignment is null)
        {
            target = default;
            return true;
        }

        var slot = frame.GetSlot(assignment.Name);
        target = new KnownTarget(true, slot);
        return frame.IsInt32Slot(slot);
    }

    public static bool TryCreate(
        IfStatement branch,
        string loopLocal,
        InterpreterFrame frame,
        I32CallEvaluator calls,
        out I32BranchedPlanData plan)
    {
        plan = default;
        if (!I32ComparisonPlan.TryCreate(branch.Condition, frame, loopLocal, calls, out var condition) ||
            !TryCreateBranch(branch.Then, frame, loopLocal, calls, out var thenBranch) ||
            !TryCreateBranch(branch.Else, frame, loopLocal, calls, out var elseBranch))
        {
            return false;
        }

        plan = new I32BranchedPlanData(condition, thenBranch, elseBranch);
        return true;
    }

    public static bool TryCreateReusable(
        IfStatement branch,
        string loopLocal,
        InterpreterFrame frame,
        I32CallEvaluator calls,
        KnownTarget knownTarget,
        out I32BranchedPlanData plan)
    {
        plan = default;
        var knownTargetAvailable = knownTarget.HasValue;
        if (!I32ComparisonPlan.TryCreate(branch.Condition, frame, loopLocal, calls, out var condition) ||
            !TryCreateReusableBranch(
                branch.Then,
                frame,
                loopLocal,
                calls,
                knownTarget,
                ref knownTargetAvailable,
                out var thenBranch) ||
            !TryCreateReusableBranch(
                branch.Else,
                frame,
                loopLocal,
                calls,
                knownTarget,
                ref knownTargetAvailable,
                out var elseBranch))
        {
            return false;
        }

        plan = new I32BranchedPlanData(condition, thenBranch, elseBranch);
        return true;
    }

    private static bool TryCreateBranch(
        IReadOnlyList<Statement> statements,
        InterpreterFrame frame,
        string loopLocal,
        I32CallEvaluator calls,
        out I32BranchedBranchPlan branch)
    {
        branch = default;
        if (statements.Count == 0)
        {
            branch = EmptyBranch();
            return true;
        }

        if (statements.Count == 1)
        {
            if (!TryCreateAssignment(
                    statements[0], frame, loopLocal, calls, out var assignment, out var singleFuel))
            {
                return false;
            }

            branch = SingleBranch(assignment, singleFuel);
            return true;
        }

        var assignments = new I32BranchedAssignmentPlan[statements.Count];
        long fuel = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (!TryCreateAssignment(
                    statements[i], frame, loopLocal, calls, out assignments[i], out var assignmentFuel))
            {
                return false;
            }

            fuel += assignmentFuel;
        }

        branch = new I32BranchedBranchPlan(I32BranchedBranchKind.Many, default, assignments, fuel);
        return true;
    }

    private static bool TryCreateReusableBranch(
        IReadOnlyList<Statement> statements,
        InterpreterFrame frame,
        string loopLocal,
        I32CallEvaluator calls,
        KnownTarget knownTarget,
        ref bool knownTargetAvailable,
        out I32BranchedBranchPlan branch)
    {
        branch = default;
        if (statements.Count > 1)
        {
            return false;
        }

        if (statements.Count == 0)
        {
            branch = EmptyBranch();
            return true;
        }

        if (!TryCreateReusableAssignment(
                statements[0],
                frame,
                loopLocal,
                calls,
                knownTarget,
                ref knownTargetAvailable,
                out var assignment,
                out var fuel))
        {
            return false;
        }

        branch = SingleBranch(assignment, fuel);
        return true;
    }

    private static bool TryCreateAssignment(
        Statement statement,
        InterpreterFrame frame,
        string loopLocal,
        I32CallEvaluator calls,
        out I32BranchedAssignmentPlan plan,
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

        plan = new I32BranchedAssignmentPlan(targetSlot, expression);
        fuel = 1 + expression.FuelCost;
        return true;
    }

    private static bool TryCreateReusableAssignment(
        Statement statement,
        InterpreterFrame frame,
        string loopLocal,
        I32CallEvaluator calls,
        KnownTarget knownTarget,
        ref bool knownTargetAvailable,
        out I32BranchedAssignmentPlan plan,
        out long fuel)
    {
        plan = default;
        fuel = 0;
        if (statement is not AssignmentStatement assignment ||
            !I32ExpressionPlan.TryCreate(assignment.Value, frame, loopLocal, calls, out var expression))
        {
            return false;
        }

        var targetSlot = knownTargetAvailable ? knownTarget.Slot : frame.GetSlot(assignment.Name);
        if (!knownTargetAvailable && !frame.IsInt32Slot(targetSlot))
        {
            return false;
        }

        knownTargetAvailable = false;
        plan = new I32BranchedAssignmentPlan(targetSlot, expression);
        fuel = 1 + expression.FuelCost;
        return true;
    }

    private static I32BranchedBranchPlan EmptyBranch()
        => new(I32BranchedBranchKind.Empty, default, null, 0);

    private static I32BranchedBranchPlan SingleBranch(I32BranchedAssignmentPlan assignment, long fuel)
        => new(I32BranchedBranchKind.Single, assignment, null, fuel);

    internal readonly record struct KnownTarget(bool HasValue, int Slot);
}
