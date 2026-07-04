namespace DotBoxD.Kernels.Verifier.Generated.Methods;

using static GeneratedMethodShapeSignatures;

internal static class GeneratedAccumulateLinearMeterVerifier
{
    public static void Verify(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        for (var i = 0; i < analysis.Instructions.Count; i++)
        {
            var instruction = analysis.Instructions[i];
            if (!IsReachable(analysis, instruction) ||
                instruction.CalledMember != AccumulateLinearI32Signature ||
                !HasNonPositiveImmediateIterations(analysis, i))
            {
                continue;
            }

            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' must call AccumulateLinearI32 with positive iterations"));
        }
    }

    private static bool HasNonPositiveImmediateIterations(
        GeneratedMethodFlow analysis,
        int instructionIndex)
    {
        if (instructionIndex < 2)
        {
            return false;
        }

        var call = analysis.Instructions[instructionIndex];
        var fuelPerIteration = analysis.Instructions[instructionIndex - 1];
        var iterations = analysis.Instructions[instructionIndex - 2];
        return HasSinglePreviousInstruction(analysis, call, fuelPerIteration) &&
               HasSinglePreviousInstruction(analysis, fuelPerIteration, iterations) &&
               iterations.Int32Value is <= 0 &&
               IsReachable(analysis, iterations);
    }

    private static bool IsReachable(GeneratedMethodFlow analysis, GeneratedInstruction instruction)
        => analysis.EntryStates.ContainsKey(instruction.Offset);

    private static bool HasSinglePreviousInstruction(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction,
        GeneratedInstruction previous)
        => analysis.PredecessorsByOffset.TryGetValue(instruction.Offset, out var predecessor) &&
           predecessor is { Count: 1 } &&
           predecessor.Offset == previous.Offset;
}
