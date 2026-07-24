using System.Reflection.Metadata;

namespace DotBoxD.Kernels.Verifier.Generated.Methods;

using static GeneratedMethodShapeSignatures;

internal static class GeneratedReturnValidationProofVerifier
{
    public static void Verify(
        string methodName,
        GeneratedMethodFlow analysis,
        GeneratedCallDepthAnalysis callDepth,
        List<VerificationDiagnostic> diagnostics)
    {
        var publications = analysis.Instructions.Where(
                i => i.CalledMember == RequireValueTypeAndRecordValidation &&
                     analysis.EntryStates.ContainsKey(i.Offset))
            .ToArray();
        foreach (var instruction in publications)
        {
            if (!analysis.IndexByOffset.TryGetValue(instruction.Offset, out var index) ||
                !IsTerminalPublication(analysis, callDepth, index))
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' may only publish return validation immediately before ExitCall and return"));
            }
        }

        if (publications.Length == 0)
        {
            return;
        }

        if (methodName != "Fn_0")
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' may not publish entrypoint return validation"));
            return;
        }

        foreach (var instruction in analysis.Instructions.Where(
                     i => i.Opcode == ILOpCode.Ret && analysis.EntryStates.ContainsKey(i.Offset)))
        {
            if (!analysis.IndexByOffset.TryGetValue(instruction.Offset, out var index) ||
                index < 3 ||
                !IsTerminalPublication(analysis, callDepth, index - 3))
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    "method 'Fn_0' must publish return validation on every reachable return"));
            }
        }
    }

    private static bool IsTerminalPublication(
        GeneratedMethodFlow analysis,
        GeneratedCallDepthAnalysis callDepth,
        int helperIndex)
    {
        if (helperIndex < 0 || helperIndex + 3 >= analysis.Instructions.Count)
        {
            return false;
        }

        var helper = analysis.Instructions[helperIndex];
        var validState = helper.CalledMember == RequireValueTypeAndRecordValidation &&
                         analysis.EntryStates.TryGetValue(helper.Offset, out var state) &&
                         (state & (GeneratedMeterState.EnterCall | GeneratedMeterState.ChargeFuel)) ==
                         (GeneratedMeterState.EnterCall | GeneratedMeterState.ChargeFuel) &&
                         (state & GeneratedMeterState.ExitCall) == 0 &&
                         callDepth.IsBalanced &&
                         callDepth.EntryDepths.TryGetValue(helper.Offset, out var depth) &&
                         depth == 1;
        return validState &&
               analysis.Instructions[helperIndex + 1].Opcode == ILOpCode.Ldarg_0 &&
               analysis.Instructions[helperIndex + 2].CalledMember == ExitCall &&
               analysis.Instructions[helperIndex + 3].Opcode == ILOpCode.Ret &&
               HasSinglePredecessor(analysis, helperIndex + 1, helper.Offset) &&
               HasSinglePredecessor(
                   analysis,
                   helperIndex + 2,
                   analysis.Instructions[helperIndex + 1].Offset) &&
               HasSinglePredecessor(
                   analysis,
                   helperIndex + 3,
                   analysis.Instructions[helperIndex + 2].Offset);
    }

    private static bool HasSinglePredecessor(
        GeneratedMethodFlow analysis,
        int instructionIndex,
        int expectedOffset)
        => analysis.PredecessorsByOffset.TryGetValue(
               analysis.Instructions[instructionIndex].Offset,
               out var predecessor) &&
           predecessor.Count == 1 &&
           predecessor.Offset == expectedOffset;
}
