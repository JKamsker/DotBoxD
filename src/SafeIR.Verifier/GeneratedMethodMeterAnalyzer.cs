namespace SafeIR.Verifier;

using System.Reflection.Metadata;

internal static class GeneratedMethodMeterAnalyzer
{
    private const int MaxInstructionsBetweenFuelMeters = 32;

    public static bool HasUnmeteredWorkPath(
        GeneratedMethodFlow analysis,
        Func<string?, bool> isFuelMeter,
        Func<string?, bool> isMeterDensityWorkCall)
    {
        if (analysis.Instructions.Count == 0)
        {
            return false;
        }

        var balances = new Dictionary<int, int> { [analysis.Instructions[0].Offset] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(analysis.Instructions[0].Offset);
        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var instruction = analysis.ByOffset[offset];
            var output = balances[offset] +
                MeterCredit(analysis, instruction, isFuelMeter) -
                WorkCost(instruction, isMeterDensityWorkCall);
            if (output < 0)
            {
                return true;
            }

            foreach (var successor in GeneratedMethodFlowAnalyzer.Successors(
                analysis.Instructions,
                analysis.ByOffset,
                instruction))
            {
                if (!analysis.ByOffset.ContainsKey(successor))
                {
                    continue;
                }

                if (!balances.TryGetValue(successor, out var existing) || output < existing)
                {
                    balances[successor] = output;
                    queue.Enqueue(successor);
                }
            }
        }

        return false;
    }

    public static bool HasPositiveImmediateMeterAmount(GeneratedMethodFlow analysis, int instructionIndex)
    {
        if (instructionIndex <= 0)
        {
            return false;
        }

        var instruction = analysis.Instructions[instructionIndex];
        var previous = analysis.Instructions[instructionIndex - 1];
        var predecessors = Predecessors(analysis, instruction.Offset).ToArray();
        return predecessors.Length == 1 &&
               predecessors[0].Offset == previous.Offset &&
               previous.Int32Value is > 0 &&
               analysis.EntryStates.ContainsKey(previous.Offset);
    }

    public static bool HasSparseMeterPath(
        GeneratedMethodFlow analysis,
        Func<string?, bool> isFuelMeter)
    {
        if (analysis.Instructions.Count == 0)
        {
            return false;
        }

        var counts = new Dictionary<int, int> { [analysis.Instructions[0].Offset] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(analysis.Instructions[0].Offset);
        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var instruction = analysis.ByOffset[offset];
            var output = isFuelMeter(instruction.CalledMember) && HasPositiveImmediateMeterAmount(analysis, instruction)
                ? 0
                : counts[offset] + InstructionCost(instruction);
            if (output > MaxInstructionsBetweenFuelMeters)
            {
                return true;
            }

            foreach (var successor in GeneratedMethodFlowAnalyzer.Successors(
                analysis.Instructions,
                analysis.ByOffset,
                instruction))
            {
                if (!analysis.ByOffset.ContainsKey(successor))
                {
                    continue;
                }

                if (!counts.TryGetValue(successor, out var existing) || output > existing)
                {
                    counts[successor] = output;
                    queue.Enqueue(successor);
                }
            }
        }

        return false;
    }

    private static int MeterCredit(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction,
        Func<string?, bool> isFuelMeter)
        => isFuelMeter(instruction.CalledMember) && HasPositiveImmediateMeterAmount(analysis, instruction)
            ? 1
            : 0;

    private static int WorkCost(GeneratedInstruction instruction, Func<string?, bool> isMeterDensityWorkCall)
        => isMeterDensityWorkCall(instruction.CalledMember) || instruction.IsLocalCall ? 1 : 0;

    private static int InstructionCost(GeneratedInstruction instruction)
        => instruction.Opcode is ILOpCode.Nop ? 0 : 1;

    private static bool HasPositiveImmediateMeterAmount(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction)
    {
        for (var i = 0; i < analysis.Instructions.Count; i++)
        {
            if (analysis.Instructions[i].Offset == instruction.Offset)
            {
                return HasPositiveImmediateMeterAmount(analysis, i);
            }
        }

        return false;
    }

    private static IEnumerable<GeneratedInstruction> Predecessors(GeneratedMethodFlow analysis, int offset)
        => analysis.Instructions.Where(candidate =>
            GeneratedMethodFlowAnalyzer.Successors(analysis.Instructions, analysis.ByOffset, candidate)
                .Contains(offset));
}
