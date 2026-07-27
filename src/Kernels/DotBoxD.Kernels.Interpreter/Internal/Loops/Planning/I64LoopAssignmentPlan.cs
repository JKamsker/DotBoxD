using DotBoxD.Kernels.Interpreter.Internal.Expressions;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal readonly record struct I64LoopAssignmentPlan(
    int TargetSlot,
    I64ExpressionPlan Expression);

internal static class I64LoopAssignmentPlans
{
    public static int[] GetRequiredEntrySlots(ReadOnlySpan<I64LoopAssignmentPlan> assignments)
    {
        var required = new List<int>();
        var requiredSet = new HashSet<int>();
        var written = new HashSet<int>();
        var reads = new List<int>();

        for (var assignmentIndex = 0; assignmentIndex < assignments.Length; assignmentIndex++)
        {
            var assignment = assignments[assignmentIndex];
            reads.Clear();
            assignment.Expression.CollectRequiredRawSlots(reads);
            for (var readIndex = 0; readIndex < reads.Count; readIndex++)
            {
                var slot = reads[readIndex];
                if (!written.Contains(slot) && requiredSet.Add(slot))
                {
                    required.Add(slot);
                }
            }

            written.Add(assignment.TargetSlot);
        }

        return required.ToArray();
    }
}
