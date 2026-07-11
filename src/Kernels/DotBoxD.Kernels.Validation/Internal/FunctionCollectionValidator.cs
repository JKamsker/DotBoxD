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

        var counts = StructuralDuplicateValidator.CountValues(
            functions,
            static function => function?.Id,
            out var nullCount);

        var reportedNull = false;
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i] is not { } function)
            {
                continue;
            }

            var id = function.Id;
            if (StructuralDuplicateValidator.ShouldReportDuplicate(counts, id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic("E-STRUCT-DUP-FN", $"duplicate function id '{id}'"));
            }
        }
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
