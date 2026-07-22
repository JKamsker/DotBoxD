using System.Collections.Concurrent;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed class I32WhileLoopPlan
{
    private readonly int[] _requiredSlots;

    public I32WhileLoopPlan(
        WhileStatement statement,
        I32ComparisonPlan condition,
        int targetSlot,
        I32ExpressionPlan expression,
        long bodyFuel)
    {
        Statement = statement;
        Condition = condition;
        TargetSlot = targetSlot;
        Expression = expression;
        BodyFuel = bodyFuel;

        var requiredSlots = new List<int>();
        condition.CollectRequiredRawSlots(requiredSlots);
        expression.CollectRequiredRawSlots(requiredSlots);
        _requiredSlots = requiredSlots.ToArray();
    }

    public WhileStatement Statement { get; }

    public I32ComparisonPlan Condition { get; }

    public int TargetSlot { get; }

    public I32ExpressionPlan Expression { get; }

    public long BodyFuel { get; }

    public bool CanRun(InterpreterFrame frame)
    {
        for (var i = 0; i < _requiredSlots.Length; i++)
        {
            if (!frame.IsSlotAssigned(_requiredSlots[i]))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class I32WhileLoopPlanCache
{
    private I32WhileLoopPlan? _hotPlan;
    private ConcurrentDictionary<WhileStatement, I32WhileLoopPlan>? _additionalPlans;

    public bool TryGet(
        WhileStatement statement,
        InterpreterFrame frame,
        out I32WhileLoopPlan plan)
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

        return plan.CanRun(frame);
    }

    public bool Contains(WhileStatement statement)
    {
        var hot = Volatile.Read(ref _hotPlan);
        if (ReferenceEquals(hot?.Statement, statement))
        {
            return true;
        }

        var additional = Volatile.Read(ref _additionalPlans);
        return additional?.ContainsKey(statement) == true;
    }

    public void Store(I32WhileLoopPlan plan)
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
            var created = new ConcurrentDictionary<WhileStatement, I32WhileLoopPlan>(
                ReferenceEqualityComparer.Instance);
            additional = Interlocked.CompareExchange(ref _additionalPlans, created, null) ?? created;
        }

        additional.TryAdd(plan.Statement, plan);
    }
}
