using System.Reflection.Metadata;

namespace DotBoxD.Kernels.Verifier.Generated.Methods;

using static GeneratedMethodShapeSignatures;
using static Verifier.VerifierTypeNames;

internal static class GeneratedUnchargedLiteralShapeVerifier
{
    // Read-only consumers the compiler feeds an uncharged literal key before charging it.
    private static readonly string CanBulkChargeSandboxValueSignature =
        $"{CompiledRuntimeName}.CanBulkChargeSandboxValue({SandboxContextName},{SandboxValueName},{Int32Name}):{BooleanName}";
    private static readonly string MapGetI32RawSignature =
        $"{CompiledRuntimeName}.MapGetI32Raw({SandboxValueName},{SandboxValueName}):{Int32Name}";

    public static void Verify(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var instruction in analysis.Instructions.Where(i => IsUnchargedLiteralCall(i.CalledMember)))
        {
            if (!IsReachable(analysis, instruction) ||
                IsImmediatelyStoredIntoLiteralArray(analysis, instruction) ||
                IsStoredIntoLocalChargedBeforeUse(analysis, instruction))
            {
                continue;
            }

            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' must store uncharged literal helper '{instruction.CalledMember}' directly into a literal array or charge it before any other use"));
        }
    }

    private static bool IsImmediatelyStoredIntoLiteralArray(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction)
    {
        if (!analysis.IndexByOffset.TryGetValue(instruction.Offset, out var index) ||
            index + 1 >= analysis.Instructions.Count)
        {
            return false;
        }

        return analysis.Instructions[index + 1].Opcode == ILOpCode.Stelem_ref;
    }

    // The literal stored in a local is uncharged until a proven ChargeSandboxValue(s) call charges it.
    // On every path from the store until that charge (or until the local is overwritten), each read
    // of the local must be the proven operand of a non-escaping consumer; any other read could copy
    // the uncharged value out (into another local, onto a shared ret, into a call), so it is rejected.
    private static bool IsStoredIntoLocalChargedBeforeUse(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction)
    {
        if (!analysis.IndexByOffset.TryGetValue(instruction.Offset, out var index) ||
            index + 1 >= analysis.Instructions.Count ||
            !IsStoreLocal(analysis.Instructions[index + 1], out var localIndex))
        {
            return false;
        }

        var queue = new Queue<int>();
        var visited = new HashSet<int>();
        EnqueueSuccessors(analysis, analysis.Instructions[index + 1], queue, visited);
        while (queue.Count > 0)
        {
            if (!analysis.ByOffset.TryGetValue(queue.Dequeue(), out var candidate) ||
                !IsReachable(analysis, candidate))
            {
                continue;
            }

            var currentIndex = analysis.IndexByOffset[candidate.Offset];
            if (IsChargeForLocal(analysis, currentIndex, localIndex) ||
                (IsStoreLocal(candidate, out var storedLocal) && storedLocal == localIndex))
            {
                // Charged, or overwritten: the uncharged literal no longer lives in the local on this path.
                continue;
            }

            if (IsLoadLocal(candidate, localIndex) &&
                !IsNonEscapingUnchargedRead(analysis, currentIndex))
            {
                return false;
            }

            EnqueueSuccessors(analysis, candidate, queue, visited);
        }

        return true;
    }

    // The load at loadIndex must be consumed, by straight-line fall-through, as the SandboxValue operand
    // of a call that only reads it (charges, bulk-charge probes, map lookups) and never returns it.
    private static bool IsNonEscapingUnchargedRead(GeneratedMethodFlow analysis, int loadIndex)
    {
        var instructions = analysis.Instructions;
        if (loadIndex + 1 < instructions.Count &&
            instructions[loadIndex + 1].CalledMember is { } lastOperandConsumer &&
            (lastOperandConsumer == ChargeSandboxValueSignature || lastOperandConsumer == MapGetI32RawSignature))
        {
            return GeneratedInstructionPredecessors.PreviousRunIsStraightLine(analysis, loadIndex + 1, 1);
        }

        return loadIndex + 2 < instructions.Count &&
               instructions[loadIndex + 2].CalledMember is { } countConsumer &&
               (countConsumer == ChargeSandboxValuesSignature || countConsumer == CanBulkChargeSandboxValueSignature) &&
               IsSinglePush(instructions[loadIndex + 1]) &&
               GeneratedInstructionPredecessors.PreviousRunIsStraightLine(analysis, loadIndex + 2, 2);
    }

    private static bool IsSinglePush(GeneratedInstruction instruction)
        => instruction.Opcode is ILOpCode.Ldloc or ILOpCode.Ldloc_s or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1
                or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldarg or ILOpCode.Ldarg_s or ILOpCode.Ldarg_0
                or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 ||
            instruction.Int32Value is not null;

    private static void EnqueueSuccessors(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction,
        Queue<int> queue,
        HashSet<int> visited)
    {
        if (!analysis.SuccessorsByOffset.TryGetValue(instruction.Offset, out var successors))
        {
            return;
        }

        foreach (var successor in successors)
        {
            if (visited.Add(successor))
            {
                queue.Enqueue(successor);
            }
        }
    }

    // The charged operand is read from the textually preceding loads, so they must reach the charge
    // call by straight-line fall-through; a branch could otherwise skip them with another value.
    private static bool IsChargeForLocal(
        GeneratedMethodFlow analysis,
        int index,
        int localIndex)
    {
        var instructions = analysis.Instructions;
        var instruction = instructions[index];
        return instruction.CalledMember switch
        {
            var signature when signature == ChargeSandboxValueSignature
                => GeneratedInstructionPredecessors.PreviousRunIsStraightLine(analysis, index, 1) &&
                   PreviousInstructionLoadsLocal(instructions, index, localIndex),
            var signature when signature == ChargeSandboxValuesSignature
                => GeneratedInstructionPredecessors.PreviousRunIsStraightLine(analysis, index, 2) &&
                   ValueArgumentLoadsLocal(instructions, index, localIndex) &&
                   PreviousInstructionLoadsPositiveInt32(instructions, index),
            _ => false
        };
    }

    private static bool PreviousInstructionLoadsPositiveInt32(
        IReadOnlyList<GeneratedInstruction> instructions,
        int index)
        => index > 0 && instructions[index - 1].Int32Value > 0;

    private static bool ValueArgumentLoadsLocal(
        IReadOnlyList<GeneratedInstruction> instructions,
        int index,
        int localIndex)
        => index > 1 && IsLoadLocal(instructions[index - 2], localIndex);

    private static bool PreviousInstructionLoadsLocal(
        IReadOnlyList<GeneratedInstruction> instructions,
        int index,
        int localIndex)
        => index > 0 && IsLoadLocal(instructions[index - 1], localIndex);

    private static bool IsReachable(GeneratedMethodFlow analysis, GeneratedInstruction instruction)
        => analysis.EntryStates.ContainsKey(instruction.Offset);

    private static bool IsLoadLocal(GeneratedInstruction instruction, int localIndex)
        => (instruction.Opcode is ILOpCode.Ldloc or ILOpCode.Ldloc_s or ILOpCode.Ldloc_0
                or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3) &&
            instruction.OperandIndex == localIndex;

    private static bool IsStoreLocal(GeneratedInstruction instruction, out int localIndex)
    {
        if ((instruction.Opcode is ILOpCode.Stloc or ILOpCode.Stloc_s or ILOpCode.Stloc_0
                or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3) &&
            instruction.OperandIndex is { } index)
        {
            localIndex = index;
            return true;
        }

        localIndex = -1;
        return false;
    }
}
