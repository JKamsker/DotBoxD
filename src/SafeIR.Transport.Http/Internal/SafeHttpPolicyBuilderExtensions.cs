namespace SafeIR.Transport.Http.Internal;

using System.Globalization;

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
        ThrowIfNegative(maxResponseBytes, nameof(maxResponseBytes));
        if (maxRequestBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequestBytes));
        }

        if (timeout is not null && timeout.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var schemes = allowedSchemes?.ToArray() ?? ["https"];
        var requestBytes = maxRequestBytes ?? 1_048_576;
        return builder.Grant(
            "net.http.get",
            new Dictionary<string, string>
            {
                ["allowedHosts"] = string.Join(',', allowedHosts),
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

    private static void ThrowIfNegative(long value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }
}
