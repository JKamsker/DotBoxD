using System.Collections.Concurrent;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed class I32ForLoopPlan
{
    private readonly int[] _requiredSlots;

    public I32ForLoopPlan(
        ForRangeStatement statement,
        int targetSlot,
        I32ExpressionPlan expression,
        long fuelPerIteration,
        int[] requiredSlots)
    {
        Statement = statement;
        TargetSlot = targetSlot;
        Expression = expression;
        FuelPerIteration = fuelPerIteration;
        _requiredSlots = requiredSlots;
    }

    public ForRangeStatement Statement { get; }

    public int TargetSlot { get; }

    public I32ExpressionPlan Expression { get; }

    public long FuelPerIteration { get; }

    public bool CanRun(InterpreterFrame frame, int loopSlot)
    {
        // A layout fixes slot identities, but assignment state belongs to each invocation.
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

internal sealed class I32ForLoopPlanCache
{
    private I32ForLoopPlan? _hotPlan;
    private ConcurrentDictionary<ForRangeStatement, I32ForLoopPlan>? _additionalPlans;

    public bool TryGet(
        ForRangeStatement statement,
        InterpreterFrame frame,
        int loopSlot,
        out I32ForLoopPlan plan)
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

        return plan.CanRun(frame, loopSlot);
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

    public void Store(I32ForLoopPlan plan)
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
            var created = new ConcurrentDictionary<ForRangeStatement, I32ForLoopPlan>(
                ReferenceEqualityComparer.Instance);
            additional = Interlocked.CompareExchange(ref _additionalPlans, created, null) ?? created;
        }

        additional.TryAdd(plan.Statement, plan);
    }
}
