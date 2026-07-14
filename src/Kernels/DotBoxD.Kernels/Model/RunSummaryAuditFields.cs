using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Model;

public static class RunSummaryAuditFields
{
    private const string Redacted = "[redacted]";

    private static readonly string[] SecretMarkers =
    [
        "authorization",
        "bearer",
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "access-token",
        "access_token",
        "refresh-token",
        "refresh_token",
        "session-token",
        "session_token",
        "api-key",
        "api_key",
        "apikey",
        "account-key",
        "account_key",
        "client-key",
        "client_key",
        "client-secret",
        "client_secret",
        "private-key",
        "private_key"
    ];

    public static IReadOnlyDictionary<string, string> Create(
        ExecutionPlan plan,
        ResourceMeter budget,
        ExecutionMode mode,
        string cacheStatus,
        string? runtimeForm = null,
        string? cacheKey = null,
        string? artifactHash = null,
        string? materializationStatus = null,
        bool executionDispatched = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(budget);
        return Create(
            plan,
            SummaryUsage.From(budget),
            budget.Limits,
            mode,
            cacheStatus,
            runtimeForm,
            cacheKey,
            artifactHash,
            materializationStatus,
            executionDispatched);
    }

    internal static IReadOnlyDictionary<string, string> Create(
        ExecutionPlan plan,
        SandboxResourceUsage usage,
        ResourceLimits limits,
        ExecutionMode mode,
        string cacheStatus,
        string? runtimeForm = null,
        string? cacheKey = null,
        string? artifactHash = null,
        string? materializationStatus = null,
        bool executionDispatched = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(limits);
        return Create(
            plan,
            SummaryUsage.From(usage),
            limits,
            mode,
            cacheStatus,
            runtimeForm,
            cacheKey,
            artifactHash,
            materializationStatus,
            executionDispatched);
    }

    private static IReadOnlyDictionary<string, string> Create(
        ExecutionPlan plan,
        SummaryUsage usage,
        ResourceLimits limits,
        ExecutionMode mode,
        string cacheStatus,
        string? runtimeForm,
        string? cacheKey,
        string? artifactHash,
        string? materializationStatus,
        bool executionDispatched)
    {
        RequireDefinedMode(mode);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheStatus);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = mode.ToString(),
            ["executionMode"] = mode.ToString(),
            ["executionDispatched"] = executionDispatched.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cacheStatus"] = cacheStatus,
            ["moduleHash"] = plan.ModuleHash,
            ["planHash"] = plan.PlanHash,
            ["policyId"] = SafePolicyId(plan.Policy.PolicyId),
            ["policyHash"] = plan.PolicyHash,
            ["bindingManifestHash"] = plan.BindingManifestHash,
            ["fuelUsed"] = usage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxFuel"] = limits.MaxFuel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["loopIterations"] = usage.LoopIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxLoopIterations"] = limits.MaxLoopIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allocatedBytes"] = usage.AllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allocationCharged"] = usage.AllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxAllocatedBytes"] = limits.MaxAllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["hostCalls"] = usage.HostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxHostCalls"] = limits.MaxHostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fileBytesRead"] = usage.FileBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxFileBytesRead"] = limits.MaxFileBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fileBytesWritten"] = usage.FileBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxFileBytesWritten"] = limits.MaxFileBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["networkBytesRead"] = usage.NetworkBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxNetworkBytesRead"] = limits.MaxNetworkBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["networkBytesWritten"] = usage.NetworkBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxNetworkBytesWritten"] = limits.MaxNetworkBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["logEvents"] = usage.LogEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxLogEvents"] = limits.MaxLogEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["collectionElements"] = usage.CollectionElements.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxCollectionElements"] = limits.MaxTotalCollectionElements.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["stringBytes"] = usage.StringBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxStringBytes"] = limits.MaxTotalStringBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        AddIfPresent(fields, "runtimeForm", runtimeForm);
        AddIfPresent(fields, "cacheKey", cacheKey);
        AddIfPresent(fields, "artifactHash", artifactHash);
        AddIfPresent(fields, "materializationStatus", materializationStatus);
        return fields;
    }

    private static void RequireDefinedMode(ExecutionMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Run summary execution mode must be defined.");
        }
    }

    private static void AddIfPresent(IDictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[key] = value;
        }
    }

    private readonly record struct SummaryUsage(
        long FuelUsed,
        long LoopIterations,
        long AllocatedBytes,
        int HostCalls,
        long FileBytesRead,
        long FileBytesWritten,
        long NetworkBytesRead,
        long NetworkBytesWritten,
        int LogEvents,
        long CollectionElements,
        long StringBytes)
    {
        public static SummaryUsage From(ResourceMeter budget)
            => new(
                budget.FuelUsed,
                budget.LoopIterations,
                budget.AllocatedBytes,
                budget.HostCalls,
                budget.FileBytesRead,
                budget.FileBytesWritten,
                budget.NetworkBytesRead,
                budget.NetworkBytesWritten,
                budget.LogEvents,
                budget.CollectionElements,
                budget.StringBytes);

        public static SummaryUsage From(SandboxResourceUsage usage)
            => new(
                usage.FuelUsed,
                usage.LoopIterations,
                usage.AllocatedBytes,
                usage.HostCalls,
                usage.FileBytesRead,
                usage.FileBytesWritten,
                usage.NetworkBytesRead,
                usage.NetworkBytesWritten,
                usage.LogEvents,
                usage.CollectionElements,
                usage.StringBytes);
    }

    internal static string SafePolicyId(string? policyId)
    {
        if (string.IsNullOrEmpty(policyId))
        {
            return Redacted;
        }

        var span = TrimPolicyId(policyId);
        if (!IsPolicyIdLengthAllowed(span.Length) ||
            !IsPolicyIdTextSafe(policyId, span) ||
            ContainsSecretMarker(policyId, span.Start, span.Length))
        {
            return Redacted;
        }

        return span.Start == 0 && span.Length == policyId.Length
            ? policyId
            : policyId.Substring(span.Start, span.Length);
    }

    private static PolicyIdSpan TrimPolicyId(string policyId)
    {
        var start = 0;
        var end = policyId.Length - 1;
        while (start <= end && IsPolicyIdTrimChar(policyId[start]))
        {
            start++;
        }

        while (end >= start && IsPolicyIdTrimChar(policyId[end]))
        {
            end--;
        }

        return new PolicyIdSpan(start, end - start + 1);
    }

    private static bool IsPolicyIdLengthAllowed(int length)
        => length is > 0 and <= 128;

    private static bool IsPolicyIdTextSafe(string policyId, PolicyIdSpan span)
    {
        for (var i = span.Start; i < span.Start + span.Length; i++)
        {
            var c = policyId[i];
            if (char.IsControl(c) || !IsPolicyIdChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPolicyIdTrimChar(char c)
        => char.IsWhiteSpace(c) || char.IsControl(c);

    private static bool IsPolicyIdChar(char c)
        => char.IsAsciiLetterOrDigit(c) ||
           c is '-' or '_' or '.' or ':';

    private static bool ContainsSecretMarker(string value, int startIndex, int count)
    {
        foreach (var marker in SecretMarkers)
        {
            if (value.IndexOf(marker, startIndex, count, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct PolicyIdSpan(int Start, int Length);
}
