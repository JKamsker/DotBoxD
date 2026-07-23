using System.Collections.Concurrent;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal sealed class I32ForLoopPlan
{
    private readonly I32LoopAssignmentPlan[]? _multipleAssignments;
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

    /// <summary>
    /// Takes ownership of <paramref name="assignments"/>. The caller must not mutate
    /// the array after construction because plans are shared by concurrent frames.
    /// </summary>
    public I32ForLoopPlan(
        ForRangeStatement statement,
        I32LoopAssignmentPlan[] assignments,
        long fuelPerIteration)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Length < 2)
        {
            throw new ArgumentException("A multi-assignment plan requires at least two assignments.", nameof(assignments));
        }

        Statement = statement;
        TargetSlot = 0;
        Expression = null!;
        FuelPerIteration = fuelPerIteration;
        _multipleAssignments = assignments;
        _requiredSlots = I32LoopAssignmentPlans.GetRequiredRawSlots(assignments);
    }

    public ForRangeStatement Statement { get; }

    public int TargetSlot { get; }

    public I32ExpressionPlan Expression { get; }

    public long FuelPerIteration { get; }

    public ReadOnlySpan<I32LoopAssignmentPlan> MultipleAssignments => _multipleAssignments;

    public bool CanRun(InterpreterFrame frame, int loopSlot)
        => CanRun(frame, loopSlot, loopSlot);

    public bool CanRun(
        InterpreterFrame frame,
        int loopSlot,
        int slotWrittenBeforeEvaluation)
    {
        // A layout fixes slot identities, but assignment state belongs to each invocation.
        // The selected runner writes both exempt slots before evaluating the cached expression.
        for (var i = 0; i < _requiredSlots.Length; i++)
        {
            var slot = _requiredSlots[i];
            if (slot != loopSlot &&
                slot != slotWrittenBeforeEvaluation &&
                !frame.IsSlotAssigned(slot))
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
        => TryGet(statement, frame, loopSlot, loopSlot, out plan);

    public bool TryGet(
        ForRangeStatement statement,
        InterpreterFrame frame,
        int loopSlot,
        int slotWrittenBeforeEvaluation,
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

        return plan.CanRun(frame, loopSlot, slotWrittenBeforeEvaluation);
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
