using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Validation.Internal;

internal static class StructuralDuplicateValidator
{
    public static void CheckCapabilityRequests(
        IReadOnlyList<CapabilityRequest> requests,
        List<SandboxDiagnostic> diagnostics)
    {
        if (requests.Count < 2)
        {
            return;
        }

        var counts = CountValues(requests, static request => request.Id, out var nullCount);
        var reportedNull = false;
        for (var i = 0; i < requests.Count; i++)
        {
            var id = requests[i].Id;
            if (ShouldReportDuplicate(counts, id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic("E-STRUCT-DUP-CAP", $"duplicate capability request '{id}'"));
            }
        }
    }

    public static void CheckParameters(SandboxFunction function, List<SandboxDiagnostic> diagnostics)
    {
        if (function.Parameters.Count < 2)
        {
            return;
        }

        var counts = CountValues(function.Parameters, static parameter => parameter.Name, out var nullCount);
        var reportedNull = false;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var name = function.Parameters[i].Name;
            if (ShouldReportDuplicate(counts, name, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-STRUCT-DUP-PARAM",
                    $"duplicate parameter '{name}' in function '{function.Id}'"));
            }
        }
    }

    internal static Dictionary<string, int> CountValues<T>(
        IReadOnlyList<T> values,
        Func<T, string?> selector,
        out int nullCount)
    {
        var counts = new Dictionary<string, int>(values.Count, StringComparer.Ordinal);
        nullCount = 0;
        for (var i = 0; i < values.Count; i++)
        {
            IncrementCount(counts, selector(values[i]), ref nullCount);
        }

        return counts;
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

    internal static bool ShouldReportDuplicate(
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
}
