using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal static class FunctionCollectionValidator
{
    public static void Validate(
        IReadOnlyList<SandboxFunction> functions,
        List<SandboxDiagnostic> diagnostics)
    {
        CheckNullFunctions(functions, diagnostics);
        CheckDuplicateFunctionIds(functions, diagnostics);

        if (!HasEntrypoint(functions))
        {
            diagnostics.Add(new SandboxDiagnostic("E-STRUCT-ENTRY", "module must declare at least one entry function"));
        }
    }

    private static void CheckNullFunctions(
        IReadOnlyList<SandboxFunction> functions,
        List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i] is null)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-STRUCT-NULL",
                    $"functions entry at index {i} must not be null"));
            }
        }
    }

    private static void CheckDuplicateFunctionIds(
        IReadOnlyList<SandboxFunction> functions,
        List<SandboxDiagnostic> diagnostics)
    {
        if (functions.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(functions.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i] is { } function)
            {
                IncrementCount(counts, function.Id, ref nullCount);
            }
        }

        var reportedNull = false;
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i] is not { } function)
            {
                continue;
            }

            var id = function.Id;
            if (ShouldReportDuplicate(counts, id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic("E-STRUCT-DUP-FN", $"duplicate function id '{id}'"));
            }
        }
    }

    private static void IncrementCount(Dictionary<string, int> counts, string? value, ref int nullCount)
    {
        if (value is null)
        {
            nullCount++;
            return;
        }

        counts.TryGetValue(value, out var count);
        counts[value] = count + 1;
    }

    private static bool ShouldReportDuplicate(
        Dictionary<string, int> counts,
        string? value,
        int nullCount,
        ref bool reportedNull)
    {
        if (value is null)
        {
            if (nullCount < 2 || reportedNull)
            {
                return false;
            }

            reportedNull = true;
            return true;
        }

        if (!counts.TryGetValue(value, out var count) || count < 2)
        {
            return false;
        }

        counts[value] = 0;
        return true;
    }

    private static bool HasEntrypoint(IReadOnlyList<SandboxFunction> functions)
    {
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i]?.IsEntrypoint == true)
            {
                return true;
            }
        }

        return false;
    }
}
