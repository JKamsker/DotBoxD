namespace DotBoxD.Kernels.Verifier.Generated.Methods;

internal static class GeneratedInstructionPredecessors
{
    // True when the textually previous instruction is the only control-flow predecessor of the
    // instruction at instructionIndex and is itself reachable, so its pushed value is exactly the
    // operand the instruction consumes. A branch target can otherwise be entered with a different
    // stack value than the one the textually previous instruction pushes.
    public static bool PreviousIsSoleReachablePredecessor(GeneratedMethodFlow analysis, int instructionIndex)
    {
        if (instructionIndex <= 0 || instructionIndex >= analysis.Instructions.Count)
        {
            return false;
        }

        var previous = analysis.Instructions[instructionIndex - 1];
        return analysis.PredecessorsByOffset.TryGetValue(
                   analysis.Instructions[instructionIndex].Offset,
                   out var predecessor) &&
               predecessor is { Count: 1 } &&
               predecessor.Offset == previous.Offset &&
               analysis.EntryStates.ContainsKey(previous.Offset);
    }

    // True when each of the `count` instructions immediately before instructionIndex reaches the
    // next one only by straight-line fall-through, so they form the operand sequence it consumes.
    public static bool PreviousRunIsStraightLine(GeneratedMethodFlow analysis, int instructionIndex, int count)
    {
        if (instructionIndex < count)
        {
            return false;
        }

        for (var i = instructionIndex - count + 1; i <= instructionIndex; i++)
        {
            if (!PreviousIsSoleReachablePredecessor(analysis, i))
            {
                return false;
            }
        }

        return true;
    }
}
