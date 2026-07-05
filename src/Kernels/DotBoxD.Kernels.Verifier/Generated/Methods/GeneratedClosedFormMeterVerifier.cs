namespace DotBoxD.Kernels.Verifier.Generated.Methods;

using static GeneratedMethodShapeSignatures;

internal static class GeneratedClosedFormMeterVerifier
{
    public static void Verify(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        for (var i = 0; i < analysis.Instructions.Count; i++)
        {
            var instruction = analysis.Instructions[i];
            if (!IsReachable(analysis, instruction))
            {
                continue;
            }

            var message = NonPositiveIterationsMessage(methodName, analysis, i);
            if (message is not null)
            {
                diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", message));
            }
        }
    }

    private static string? NonPositiveIterationsMessage(
        string methodName,
        GeneratedMethodFlow analysis,
        int instructionIndex)
    {
        var calledMember = analysis.Instructions[instructionIndex].CalledMember;
        if (calledMember == AccumulateLinearI32Signature &&
            HasNonPositiveImmediateArgument(analysis, instructionIndex, offsetFromCall: 2))
        {
            return $"method '{methodName}' must call AccumulateLinearI32 with positive iterations";
        }

        if (calledMember == AddModuloBranchDeltasI32LoopRawSignature &&
            HasNonPositiveImmediateArgument(analysis, instructionIndex, offsetFromCall: 8))
        {
            return $"method '{methodName}' must call AddModuloBranchDeltasI32LoopRaw with positive iterations";
        }

        if (calledMember == AddModuloIndexAccumulatorI32LoopRawSignature &&
            HasNonPositiveImmediateRange(analysis, instructionIndex))
        {
            return $"method '{methodName}' must call AddModuloIndexAccumulatorI32LoopRaw with positive iterations";
        }

        return null;
    }

    private static bool HasNonPositiveImmediateArgument(
        GeneratedMethodFlow analysis,
        int instructionIndex,
        int offsetFromCall)
        => TryGetArgumentInstruction(analysis, instructionIndex, offsetFromCall, out var argument) &&
           argument.Int32Value is <= 0;

    private static bool HasNonPositiveImmediateRange(
        GeneratedMethodFlow analysis,
        int instructionIndex)
    {
        if (!TryGetArgumentInstruction(analysis, instructionIndex, offsetFromCall: 5, out var index) ||
            !TryGetArgumentInstruction(analysis, instructionIndex, offsetFromCall: 4, out var end) ||
            index.Int32Value is not { } indexValue ||
            end.Int32Value is not { } endValue)
        {
            return false;
        }

        return (long)endValue - indexValue <= 0;
    }

    private static bool TryGetArgumentInstruction(
        GeneratedMethodFlow analysis,
        int instructionIndex,
        int offsetFromCall,
        out GeneratedInstruction argument)
    {
        argument = default!;
        if (instructionIndex < offsetFromCall)
        {
            return false;
        }

        var argumentIndex = instructionIndex - offsetFromCall;
        argument = analysis.Instructions[argumentIndex];
        if (!IsReachable(analysis, argument))
        {
            return false;
        }

        for (var i = argumentIndex + 1; i <= instructionIndex; i++)
        {
            if (!HasSinglePreviousInstruction(analysis, analysis.Instructions[i], analysis.Instructions[i - 1]))
            {
                return false;
            }
        }

        return true;
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
