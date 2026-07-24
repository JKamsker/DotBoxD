using DotBoxD.Kernels.Interpreter.Internal.Expressions;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal readonly record struct I32LoopAssignmentPlan(
    int TargetSlot,
    I32ExpressionPlan Expression);

internal static class I32LoopAssignmentPlans
{
    public static bool HaveOnlyRawVariables(ReadOnlySpan<I32LoopAssignmentPlan> assignments)
    {
        for (var i = 0; i < assignments.Length; i++)
        {
            if (!assignments[i].Expression.HasOnlyRawVariables())
            {
                return false;
            }
        }

        return true;
    }

    public static int[] GetRequiredRawSlots(ReadOnlySpan<I32LoopAssignmentPlan> assignments)
    {
        var slots = new List<int>();
        CollectRequiredRawSlots(assignments, slots);
        return slots.ToArray();
    }

    public static void CollectRequiredRawSlots(
        ReadOnlySpan<I32LoopAssignmentPlan> assignments,
        List<int> slots)
    {
        for (var i = 0; i < assignments.Length; i++)
        {
            assignments[i].Expression.CollectRequiredRawSlots(slots);
        }
    }
}
