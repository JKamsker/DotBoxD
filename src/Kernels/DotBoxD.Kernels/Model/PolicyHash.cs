using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Model;

internal static class PolicyHash
{
    public static string Compute(SandboxPolicy policy)
    {
        var records = new List<string> {
            CanonicalEncoding.Record(
                "policy-v3",
                policy.PolicyId,
                Format((long)policy.AllowedEffects),
                policy.Deterministic.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Format(policy.LogicalNow?.ToUnixTimeMilliseconds()),
                Format(policy.RandomSeed)),
            ResourceLimitRecord(policy.ResourceLimits)
        };

        AddGrantRecords(records, policy.Grants);
        AddDeclaredOpaqueIdTypeRecords(records, policy.DeclaredOpaqueIdTypes);
        return CanonicalEncoding.HashRecords(records);
    }

    private static void AddDeclaredOpaqueIdTypeRecords(List<string> records, IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        if (declaredOpaqueIdTypes.Count == 0)
        {
            return;
        }

        var names = new string[declaredOpaqueIdTypes.Count];
        var index = 0;
        foreach (var name in declaredOpaqueIdTypes)
        {
            names[index++] = name;
        }

        Array.Sort(names, StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            records.Add(CanonicalEncoding.Record("opaque-id-type", names[i]));
        }
    }

    private static void AddGrantRecords(List<string> records, IReadOnlyList<CapabilityGrant> grants)
    {
        if (grants.Count == 0)
        {
            return;
        }

        // Exact grants resolve by id, so their list order is irrelevant and they are sorted. Wildcard
        // grants resolve first-active-match in list order (SandboxPolicy.TryGetActiveGrant), so their
        // relative order is part of the policy's meaning and is preserved after the exact grants.
        var exactRecords = new List<string>(grants.Count);
        var wildcardRecords = new List<string>();
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (grant is null)
            {
                throw new ArgumentException(
                    "Policy grants cannot contain null entries.",
                    nameof(SandboxPolicy.Grants));
            }

            var isWildcard = grant.Id is not null && CapabilityPattern.IsWildcard(grant.Id);
            (isWildcard ? wildcardRecords : exactRecords).Add(GrantRecord(grant));
        }

        exactRecords.Sort(StringComparer.Ordinal);
        records.AddRange(exactRecords);
        records.AddRange(wildcardRecords);
    }

    private static string ResourceLimitRecord(ResourceLimits limits)
        => CanonicalEncoding.Record(
            "limits",
            Format(limits.MaxFuel),
            Format(limits.MaxLoopIterations),
            Format(limits.EffectiveWallTime.Ticks),
            Format(limits.MaxAllocatedBytes),
            Format(limits.MaxCallDepth),
            Format(limits.MaxHostCalls),
            Format(limits.MaxListLength),
            Format(limits.MaxMapEntries),
            Format(limits.MaxCollectionDepth),
            Format(limits.MaxTotalCollectionElements),
            Format(limits.MaxFileBytesRead),
            Format(limits.MaxFileBytesWritten),
            Format(limits.MaxNetworkBytesRead),
            Format(limits.MaxNetworkBytesWritten),
            Format(limits.MaxLogEvents),
            Format(limits.MaxLogMessageLength),
            Format(limits.MaxStringLength),
            Format(limits.MaxTotalStringBytes));

    private static string GrantRecord(CapabilityGrant grant)
    {
        var fields = new List<string?>(5 + grant.Parameters.Count) {
            "grant",
            grant.Id,
            Format(grant.ExpiresAt?.ToUnixTimeMilliseconds()),
            grant.GrantedBy,
            grant.Reason
        };
        AddParameterRecords(fields, grant.Parameters);
        return CanonicalEncoding.Record(fields);
    }

    private static void AddParameterRecords(
        List<string?> fields,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        var ordered = new KeyValuePair<string, string>[parameters.Count];
        var index = 0;
        foreach (var parameter in parameters)
        {
            ordered[index++] = parameter;
        }

        Array.Sort(ordered, static (left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
        for (var i = 0; i < ordered.Length; i++)
        {
            fields.Add(CanonicalEncoding.Record("param", ordered[i].Key, ordered[i].Value));
        }
    }

    private static string? Format(long? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string? Format(ulong? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
