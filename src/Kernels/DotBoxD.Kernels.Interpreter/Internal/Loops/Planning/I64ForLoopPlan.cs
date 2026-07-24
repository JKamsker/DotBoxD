using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// One immutable reusable I64 for-range plan. Multi-assignment plans own their
/// source-ordered body array and retain only slots that must be assigned before
/// the first body statement runs.
/// </summary>
internal sealed class I64ForLoopPlan : IForLoopPlan
{
    private readonly I64LoopAssignmentPlan[]? _multipleAssignments;
    private readonly int[] _requiredSlots;

    public I64ForLoopPlan(
        ForRangeStatement statement,
        int targetSlot,
        I64ExpressionPlan expression,
        long fuelPerIteration)
    {
        Statement = statement;
        TargetSlot = targetSlot;
        Expression = expression;
        FuelPerIteration = fuelPerIteration;

        var requiredSlots = new List<int>();
        expression.CollectRequiredRawSlots(requiredSlots);
        _requiredSlots = requiredSlots.ToArray();
    }

    /// <summary>
    /// Takes ownership of <paramref name="assignments"/>. The caller must not
    /// mutate the array after construction because plans are shared by frames.
    /// </summary>
    public I64ForLoopPlan(
        ForRangeStatement statement,
        I64LoopAssignmentPlan[] assignments,
        long fuelPerIteration)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Length < 2)
        {
            throw new ArgumentException(
                "A multi-assignment plan requires at least two assignments.",
                nameof(assignments));
        }

        Statement = statement;
        TargetSlot = 0;
        Expression = null!;
        FuelPerIteration = fuelPerIteration;
        _multipleAssignments = assignments;
        _requiredSlots = I64LoopAssignmentPlans.GetRequiredEntrySlots(assignments);
    }

    public ForRangeStatement Statement { get; }

    public int TargetSlot { get; }

    public I64ExpressionPlan Expression { get; }

    public long FuelPerIteration { get; }

    public bool IsSingleAssignment => _multipleAssignments is null;

    public bool IsMultiAssignment => _multipleAssignments is not null;

    public ReadOnlySpan<I64LoopAssignmentPlan> MultipleAssignments => _multipleAssignments;

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
