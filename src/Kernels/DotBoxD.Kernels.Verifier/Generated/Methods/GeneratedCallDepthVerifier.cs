namespace DotBoxD.Kernels.Verifier.Generated.Methods;

using static GeneratedMethodShapeSignatures;

internal static class GeneratedCallDepthVerifier
{
    public static GeneratedCallDepthAnalysis Verify(
        string methodName,
        GeneratedMethodFlow flow,
        List<VerificationDiagnostic> diagnostics)
    {
        var entryDepths = new Dictionary<int, int>();
        if (flow.Instructions.Count == 0)
        {
            return new GeneratedCallDepthAnalysis(entryDepths, IsBalanced: false);
        }

        var isBalanced = true;
        var queue = new Queue<int>();
        var firstOffset = flow.Instructions[0].Offset;
        entryDepths[firstOffset] = 0;
        queue.Enqueue(firstOffset);
        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var instruction = flow.ByOffset[offset];
            var outputDepth = entryDepths[offset] + DepthDelta(instruction.CalledMember);
            if (outputDepth < 0)
            {
                isBalanced = false;
            }

            if (instruction.Opcode == System.Reflection.Metadata.ILOpCode.Ret && outputDepth != 0)
            {
                isBalanced = false;
            }

            foreach (var successor in flow.SuccessorsByOffset[offset])
            {
                if (!flow.EntryStates.ContainsKey(successor))
                {
                    continue;
                }

                if (!entryDepths.TryGetValue(successor, out var existingDepth))
                {
                    entryDepths.Add(successor, outputDepth);
                    queue.Enqueue(successor);
                }
                else if (existingDepth != outputDepth)
                {
                    isBalanced = false;
                }
            }
        }

        if (!isBalanced)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' must balance regular and inline call depth on every path"));
        }

        return new GeneratedCallDepthAnalysis(entryDepths, isBalanced);
    }

    private static int DepthDelta(string? calledMember)
        => calledMember switch
        {
            var member when member == EnterCall || member == EnterInlineCall => 1,
            var member when member == ExitCall || member == ExitInlineCall => -1,
            _ => 0
        };
}

internal sealed record GeneratedCallDepthAnalysis(
    IReadOnlyDictionary<int, int> EntryDepths,
    bool IsBalanced);
