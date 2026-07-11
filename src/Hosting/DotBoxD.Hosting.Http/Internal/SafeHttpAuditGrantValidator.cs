using System.Globalization;
using System.Net;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Http.Internal;

internal static class SafeHttpAuditGrantValidator
{
    private const double DurationSlackMilliseconds = 10;

    public static bool Matches(CapabilityGrant grant, SandboxAuditEvent auditEvent)
    {
        if (auditEvent.ResourceId is not { } resource ||
            !Uri.TryCreate(resource, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var options = SafeHttpGrantReader.Read(grant);
        return options.AllowedSchemes.Contains(uri.Scheme) &&
               SafeHttpUriAudit.MatchesAllowedAuthority(options.AllowedHosts, uri) &&
               IpLiteralMatches(options, uri) &&
               DurationMatches(options, auditEvent) &&
               ByteCapsMatch(options, auditEvent);
    }

    private static bool ByteCapsMatch(SafeHttpGrantOptions options, SandboxAuditEvent auditEvent)
    {
        if (!TryGetByteField(auditEvent, "bytesRead", out var bytesRead) ||
            bytesRead is null ||
            bytesRead > options.MaxResponseBytes)
        {
            return false;
        }

        return options.MaxRequestBytes is not { } maxRequestBytes ||
               TryGetByteField(auditEvent, "bytesWritten", out var bytesWritten) &&
               bytesWritten is not null &&
               bytesWritten <= maxRequestBytes;
    }

    private static bool TryGetByteField(SandboxAuditEvent auditEvent, string key, out long? bytes)
    {
        bytes = null;
        if (auditEvent.Fields is null || !auditEvent.Fields.TryGetValue(key, out var value))
        {
            return true;
        }

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            return false;
        }

        bytes = parsed;
        return true;
    }

    private static bool IpLiteralMatches(SafeHttpGrantOptions options, Uri uri)
    {
        if (!IPAddress.TryParse(UnbracketHost(uri.Host), out var address))
        {
            return true;
        }

        return options.AllowIpLiterals &&
               (options.AllowPrivateNetwork || !SafeIpAddressClassifier.IsNonGlobal(address));
    }

    private static string UnbracketHost(string host)
        => host.Length >= 2 && host[0] == '[' && host[^1] == ']'
            ? host[1..^1]
            : host;

    private static bool DurationMatches(SafeHttpGrantOptions options, SandboxAuditEvent auditEvent)
    {
        if (auditEvent.Fields is not { } fields ||
            !fields.TryGetValue("durationMs", out var text) ||
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationMs) ||
            !double.IsFinite(durationMs) ||
            durationMs < 0)
        {
            return false;
        }

        return durationMs <= options.Timeout.TotalMilliseconds + DurationSlackMilliseconds;
    }
}
