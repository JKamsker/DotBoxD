using DotBoxD.Kernels.Interpreter.Frame;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// One immutable reusable binding-free F64 for-range plan. The plan retains
/// slot identities from its owning frame layout, never values from an invocation.
/// </summary>
internal sealed class F64ForLoopPlan : IForLoopPlan
{
    private readonly int[] _requiredSlots;

    public F64ForLoopPlan(
        ForRangeStatement statement,
        int targetSlot,
        F64ExpressionPlan expression,
        long fuelPerIteration)
    {
        if (!expression.IsReusableForLoopPlan)
        {
            throw new ArgumentException("Only binding-free raw F64 expressions are reusable.", nameof(expression));
        }

        Statement = statement;
        TargetSlot = targetSlot;
        Expression = expression;
        FuelPerIteration = fuelPerIteration;

        var requiredSlots = new List<int>();
        expression.CollectRequiredRawSlots(requiredSlots);
        _requiredSlots = requiredSlots.ToArray();
    }

    public ForRangeStatement Statement { get; }
    public int TargetSlot { get; }
    public F64ExpressionPlan Expression { get; }
    public long FuelPerIteration { get; }

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
