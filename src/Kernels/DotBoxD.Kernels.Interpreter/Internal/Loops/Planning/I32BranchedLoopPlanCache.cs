using System.Collections.Concurrent;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal readonly record struct I32BranchedAssignmentPlan(
    int TargetSlot,
    I32ExpressionPlan Expression);

internal readonly record struct I32BranchedBranchPlan(
    I32BranchedBranchKind Kind,
    I32BranchedAssignmentPlan SingleAssignment,
    I32BranchedAssignmentPlan[]? MultipleAssignments,
    long Fuel)
{
    public bool IsReusable
        => Kind == I32BranchedBranchKind.Empty ||
           (Kind == I32BranchedBranchKind.Single &&
            SingleAssignment.Expression.HasOnlyRawVariables());

    public void CollectRequiredRawSlots(List<int> slots)
    {
        if (Kind == I32BranchedBranchKind.Single)
        {
            SingleAssignment.Expression.CollectRequiredRawSlots(slots);
        }
    }
}

internal enum I32BranchedBranchKind
{
    Empty,
    Single,
    Many
}

internal readonly record struct I32BranchedPlanData(
    I32ComparisonPlan Condition,
    I32BranchedBranchPlan Then,
    I32BranchedBranchPlan Else)
{
    public bool IsReusable
        => Condition.HasOnlyRawVariables() && Then.IsReusable && Else.IsReusable;

    public int[] GetRequiredRawSlots()
    {
        var slots = new List<int>();
        Condition.CollectRequiredRawSlots(slots);
        Then.CollectRequiredRawSlots(slots);
        Else.CollectRequiredRawSlots(slots);
        return slots.ToArray();
    }
}

internal sealed class I32BranchedLoopPlan
{
    private readonly I32BranchedPlanData _data;
    private readonly int[] _requiredSlots;

    public I32BranchedLoopPlan(ForRangeStatement statement, I32BranchedPlanData data)
    {
        Statement = statement;
        _data = data;
        _requiredSlots = data.GetRequiredRawSlots();
    }

    public ForRangeStatement Statement { get; }

    public ref readonly I32BranchedPlanData Data => ref _data;

    public bool CanRun(InterpreterFrame frame, int loopSlot)
    {
        for (var i = 0; i < _requiredSlots.Length; i++)
        {
            var slot = _requiredSlots[i];
            if (slot != loopSlot && !frame.IsSlotAssigned(slot))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class I32BranchedLoopPlanCache
{
    private I32BranchedLoopPlan? _hotPlan;
    private ConcurrentDictionary<ForRangeStatement, I32BranchedLoopPlan>? _additionalPlans;

    public bool TryGet(
        ForRangeStatement statement,
        InterpreterFrame frame,
        out I32BranchedLoopPlan plan)
    {
        plan = Volatile.Read(ref _hotPlan)!;
        if (!ReferenceEquals(plan?.Statement, statement))
        {
            var additional = Volatile.Read(ref _additionalPlans);
            if (additional is null || !additional.TryGetValue(statement, out plan!))
            {
                plan = null!;
                return false;
            }
        }

        return plan.CanRun(frame, frame.GetSlot(statement.LocalName));
    }

    public bool Contains(ForRangeStatement statement)
    {
        var hot = Volatile.Read(ref _hotPlan);
        if (ReferenceEquals(hot?.Statement, statement))
        {
            return true;
        }

        var additional = Volatile.Read(ref _additionalPlans);
        return additional?.ContainsKey(statement) == true;
    }

    public void Store(I32BranchedLoopPlan plan)
    {
        var hot = Volatile.Read(ref _hotPlan);
        if (hot is null)
        {
            hot = Interlocked.CompareExchange(ref _hotPlan, plan, null);
            if (hot is null)
            {
                return;
            }
        }

        if (ReferenceEquals(hot.Statement, plan.Statement))
        {
            return;
        }

        var additional = Volatile.Read(ref _additionalPlans);
        if (additional is null)
        {
            var created = new ConcurrentDictionary<ForRangeStatement, I32BranchedLoopPlan>(
                ReferenceEqualityComparer.Instance);
            additional = Interlocked.CompareExchange(ref _additionalPlans, created, null) ?? created;
        }

        additional.TryAdd(plan.Statement, plan);
    }
}
