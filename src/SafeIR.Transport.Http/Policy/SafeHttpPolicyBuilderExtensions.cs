namespace SafeIR.Transport.Http;

using System.Globalization;
using SafeIR.Transport.Http.Internal;

public static class SafeHttpPolicyBuilderExtensions
{
    public static SandboxPolicyBuilder GrantHttpGet(
        this SandboxPolicyBuilder builder,
        IEnumerable<string> allowedHosts,
        long maxResponseBytes,
        IEnumerable<string>? allowedSchemes = null,
        TimeSpan? timeout = null,
        bool allowIpLiterals = false,
        bool allowPrivateNetwork = false,
        long? maxRequestBytes = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(allowedHosts);
        ThrowIfNegative(maxResponseBytes, nameof(maxResponseBytes));
        if (maxRequestBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequestBytes));
        }

        if (timeout is not null && timeout.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var hosts = ValidateEntries(
            allowedHosts,
            nameof(allowedHosts),
            SafeHttpGrantValueValidator.IsAllowedAuthority,
            "a host or host:port authority");
        var schemes = ValidateEntries(
            allowedSchemes ?? ["https"],
            nameof(allowedSchemes),
            SafeHttpGrantValueValidator.IsAllowedScheme,
            "http or https");
        var requestBytes = maxRequestBytes ?? 1_048_576;
        return builder.Grant(
            "net.http.get",
            new Dictionary<string, string>
            {
                ["allowedHosts"] = string.Join(',', hosts),
                ["allowedSchemes"] = string.Join(',', schemes),
                ["maxRequestBytes"] = requestBytes.ToString(CultureInfo.InvariantCulture),
                ["maxResponseBytes"] = maxResponseBytes.ToString(CultureInfo.InvariantCulture),
                ["timeoutMs"] = ((long)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                ["allowIpLiterals"] = allowIpLiterals.ToString(CultureInfo.InvariantCulture),
                ["allowPrivateNetwork"] = allowPrivateNetwork.ToString(CultureInfo.InvariantCulture)
            },
            SandboxEffect.Network,
            limits => limits with
            {
                MaxNetworkBytesRead = maxResponseBytes,
                MaxNetworkBytesWritten = requestBytes
            });
    }

    private static string[] ValidateEntries(
        IEnumerable<string> values,
        string paramName,
        Func<string, bool> isValid,
        string description)
    {
        var entries = values.ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException($"At least one {description} is required.", paramName);
        }

        foreach (var entry in entries)
        {
            if (entry.Contains(',') || !isValid(entry))
            {
                throw new ArgumentException($"Each value must be {description} and must not contain commas.", paramName);
            }
        }

        return entries;
    }

    private static void ThrowIfNegative(long value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }
}
