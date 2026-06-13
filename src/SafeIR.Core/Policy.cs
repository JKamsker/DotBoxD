namespace SafeIR;

using System.Collections.ObjectModel;
using System.Reflection;

public sealed record CapabilityGrant(
    string Id,
    IReadOnlyDictionary<string, string> Parameters,
    DateTimeOffset? ExpiresAt = null,
    string GrantedBy = "host-policy",
    string Reason = "")
{
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(Parameters, StringComparer.Ordinal));
}

public sealed record SandboxPolicy(
    string PolicyId,
    SandboxEffect AllowedEffects,
    IReadOnlyList<CapabilityGrant> Grants,
    ResourceLimits ResourceLimits,
    bool Deterministic = false,
    DateTimeOffset? LogicalNow = null,
    ulong? RandomSeed = null)
{
    private readonly string _policyId = PolicyId;
    private readonly SandboxEffect _allowedEffects = AllowedEffects;
    private IReadOnlyList<CapabilityGrant> _grants = ModelCopy.List(Grants);
    private readonly ResourceLimits _resourceLimits = ResourceLimits;
    private readonly bool _deterministic = Deterministic;
    private readonly DateTimeOffset? _logicalNow = LogicalNow;
    private readonly ulong? _randomSeed = RandomSeed;

    // Lazily computed and cached so the canonical hash is built at most once per distinct
    // policy instance. Every hash-relevant property resets this so `with` copies recompute,
    // preserving the original recompute-on-change semantics while removing redundant work.
    private Lazy<string>? _hash;

    // Immutable capability index built at most once per distinct policy instance and
    // shared across the lifetime of the run. Keyed by capability id so each lookup is
    // a single O(1) probe plus an O(candidates) active-grant check (typically one
    // candidate) instead of an O(grant-count) scan. Reset when the grant list changes.
    private Lazy<Dictionary<string, CapabilityGrant[]>>? _grantIndex;

    public string PolicyId { get => _policyId; init { _policyId = value; ResetHash(); } }

    public SandboxEffect AllowedEffects { get => _allowedEffects; init { _allowedEffects = value; ResetHash(); } }

    public IReadOnlyList<CapabilityGrant> Grants
    {
        get => _grants;
        init { _grants = ModelCopy.List(value); _grantIndex = null; ResetHash(); }
    }

    public ResourceLimits ResourceLimits { get => _resourceLimits; init { _resourceLimits = value; ResetHash(); } }

    public bool Deterministic { get => _deterministic; init { _deterministic = value; ResetHash(); } }

    public DateTimeOffset? LogicalNow { get => _logicalNow; init { _logicalNow = value; ResetHash(); } }

    public ulong? RandomSeed { get => _randomSeed; init { _randomSeed = value; ResetHash(); } }

    public string Hash => (_hash ??= CreateHashCache()).Value;

    public DateTimeOffset GrantClock
        => Deterministic && LogicalNow is not null ? LogicalNow.Value : DateTimeOffset.UtcNow;

    public bool GrantsCapability(string capabilityId)
    {
        return TryGetActiveGrant(capabilityId, out _);
    }

    public CapabilityGrant GetGrant(string capabilityId)
    {
        return TryGetActiveGrant(capabilityId, out var grant)
            ? grant
            : throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.PermissionDenied,
                $"capability {capabilityId} is not granted"));
    }

    public bool TryGetGrant(string capabilityId, out CapabilityGrant grant)
        => TryGetActiveGrant(capabilityId, out grant);

    // Single O(1) indexed lookup by capability id, then a per-id active-grant check
    // (typically one candidate). Expiry is still evaluated against the live GrantClock
    // so time-bounded grants keep their original call-time semantics, and the first
    // matching grant in original list order is returned to preserve FirstOrDefault order.
    private bool TryGetActiveGrant(string capabilityId, out CapabilityGrant grant)
    {
        var index = (_grantIndex ??= CreateGrantIndexCache()).Value;
        if (index.TryGetValue(capabilityId, out var candidates))
        {
            var now = GrantClock;
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate.ExpiresAt is null || candidate.ExpiresAt > now)
                {
                    grant = candidate;
                    return true;
                }
            }
        }

        grant = null!;
        return false;
    }

    private Lazy<Dictionary<string, CapabilityGrant[]>> CreateGrantIndexCache()
        => new(() => BuildGrantIndex(_grants), LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dictionary<string, CapabilityGrant[]> BuildGrantIndex(IReadOnlyList<CapabilityGrant> grants)
    {
        var buckets = new Dictionary<string, List<CapabilityGrant>>(StringComparer.Ordinal);
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (!buckets.TryGetValue(grant.Id, out var bucket))
            {
                bucket = [];
                buckets.Add(grant.Id, bucket);
            }

            bucket.Add(grant);
        }

        var index = new Dictionary<string, CapabilityGrant[]>(buckets.Count, StringComparer.Ordinal);
        foreach (var (id, bucket) in buckets)
        {
            index.Add(id, bucket.ToArray());
        }

        return index;
    }

    private void ResetHash() => _hash = null;

    private Lazy<string> CreateHashCache()
        => new(() => PolicyHash.Compute(this), LazyThreadSafetyMode.ExecutionAndPublication);
}

internal static class ParameterReader
{
    public static IReadOnlyDictionary<string, string> Read(object parameters)
    {
        if (parameters is IReadOnlyDictionary<string, string> values)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(values, StringComparer.Ordinal));
        }

        var properties = parameters.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            dictionary.Add(
                property.Name,
                Convert.ToString(property.GetValue(parameters), System.Globalization.CultureInfo.InvariantCulture) ?? "");
        }

        return new ReadOnlyDictionary<string, string>(dictionary);
    }
}
