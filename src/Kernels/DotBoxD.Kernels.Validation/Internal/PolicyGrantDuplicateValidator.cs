using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal static class PolicyGrantDuplicateValidator
{
    public static void AddActiveGrantDiagnostics(
        IReadOnlyList<CapabilityGrant> grants,
        DateTimeOffset now,
        List<SandboxDiagnostic> diagnostics)
    {
        if (grants.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(grants.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (grant is not null && IsActive(grant, now))
            {
                IncrementCount(counts, grant.Id, ref nullCount);
            }
        }

        var reportedNull = false;
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (grant is not null &&
                IsActive(grant, now) &&
                ShouldReportDuplicate(counts, grant.Id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT",
                    DuplicateGrantMessage(grant.Id)));
            }
        }
    }

    private static string DuplicateGrantMessage(string? id)
        => id is null
            ? "policy declares multiple active grants with a null capability id"
            : $"capability '{id}' has multiple active grants";

    private static bool IsActive(CapabilityGrant grant, DateTimeOffset now)
        => grant.ExpiresAt is null || grant.ExpiresAt > now;

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
}
