using System.Net;
using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting;

internal static class WorkerHttpAuditGrantValidator
{
    public const string CapabilityId = "net.http.get";

    public static bool Matches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (auditEvent.ResourceId is not { } resource ||
            !Uri.TryCreate(resource, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !plan.Policy.TryGetGrant(CapabilityId, out var grant))
        {
            return false;
        }

        return SchemeMatches(grant, uri) &&
               AuthorityMatches(grant, uri) &&
               IpLiteralMatches(grant, uri);
    }

    private static bool SchemeMatches(CapabilityGrant grant, Uri uri)
        => ReadCsv(grant, "allowedSchemes", ["https"]) is { } allowedSchemes &&
           allowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);

    private static bool AuthorityMatches(CapabilityGrant grant, Uri uri)
    {
        var allowedHosts = ReadCsv(grant, "allowedHosts", []);
        if (allowedHosts is null || allowedHosts.Length == 0)
        {
            return false;
        }

        foreach (var allowed in allowedHosts)
        {
            if (MatchesAllowedAuthority(allowed, uri))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IpLiteralMatches(CapabilityGrant grant, Uri uri)
    {
        if (!IPAddress.TryParse(UnbracketHost(uri.Host), out var address))
        {
            return true;
        }

        return ReadBool(grant, "allowIpLiterals") &&
               (ReadBool(grant, "allowPrivateNetwork") || !IsNonGlobal(address));
    }

    private static string[]? ReadCsv(CapabilityGrant grant, string key, string[] fallback)
    {
        var value = grant.Parameters.TryGetValue(key, out var configured)
            ? configured
            : string.Join(',', fallback);
        if (value is null)
        {
            return null;
        }

        var entries = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return entries.Length == 0 && fallback.Length == 0 ? [] : entries;
    }

    private static bool ReadBool(CapabilityGrant grant, string key)
        => grant.Parameters.TryGetValue(key, out var value) &&
           bool.TryParse(value, out var parsed) &&
           parsed;

    private static bool MatchesAllowedAuthority(string allowed, Uri uri)
    {
        var authority = NormalizedAuthority(uri);
        if (StringComparer.OrdinalIgnoreCase.Equals(allowed, authority))
        {
            return true;
        }

        return uri.IsDefaultPort &&
               (StringComparer.OrdinalIgnoreCase.Equals(allowed, uri.Host) ||
                MatchesExplicitDefaultPortAuthority(allowed, uri));
    }

    private static string NormalizedAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    private static bool MatchesExplicitDefaultPortAuthority(string allowed, Uri uri)
        => TryGetDefaultPort(uri.Scheme, out var defaultPort) &&
           TryReadAuthorityPort(allowed, out var host, out var port) &&
           port == defaultPort &&
           StringComparer.OrdinalIgnoreCase.Equals(host, uri.Host);

    private static bool TryGetDefaultPort(string scheme, out int port)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(scheme, Uri.UriSchemeHttps))
        {
            port = 443;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(scheme, Uri.UriSchemeHttp))
        {
            port = 80;
            return true;
        }

        port = 0;
        return false;
    }

    private static bool TryReadAuthorityPort(string authority, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var colon = authority.StartsWith("[", StringComparison.Ordinal)
            ? authority.IndexOf("]:", StringComparison.Ordinal) + 1
            : authority.LastIndexOf(':');
        if (colon <= 0 || colon == authority.Length - 1)
        {
            return false;
        }

        host = authority[..colon];
        var portText = authority[(colon + 1)..];
        return int.TryParse(
            portText,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out port);
    }

    private static string UnbracketHost(string host)
        => host.Length >= 2 && host[0] == '[' && host[^1] == ']'
            ? host[1..^1]
            : host;

    private static bool IsNonGlobal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            Span<byte> mappedBytes = stackalloc byte[16];
            return !address.TryWriteBytes(mappedBytes, out _) || IsNonGlobalIpv4(mappedBytes[12..]);
        }

        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var bytesWritten))
        {
            return true;
        }

        var addressBytes = bytes[..bytesWritten];
        return bytesWritten == 4
            ? IsNonGlobalIpv4(addressBytes)
            : IsNonGlobalIpv6(address, addressBytes);
    }

    private static bool IsNonGlobalIpv4(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0 ||
           bytes[0] == 10 ||
           bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
           bytes[0] == 127 ||
           bytes[0] == 169 && bytes[1] == 254 ||
           bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
           bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0 ||
           bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2 ||
           bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99 && bytes[3] == 2 ||
           bytes[0] == 192 && bytes[1] == 168 ||
           bytes[0] == 198 && bytes[1] is 18 or 19 ||
           bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
           bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
           bytes[0] >= 224;

    private static bool IsNonGlobalIpv6(IPAddress address, ReadOnlySpan<byte> bytes)
        => address.Equals(IPAddress.IPv6None) ||
           address.Equals(IPAddress.IPv6Any) ||
           address.IsIPv6LinkLocal ||
           address.IsIPv6SiteLocal ||
           bytes[0] == 0xff ||
           (bytes[0] & 0xfe) == 0xfc ||
           (bytes[0] & 0xe0) != 0x20 ||
           IsIetfProtocolAssignment(bytes) ||
           IsDocumentation(bytes) ||
           IsDocumentation2(bytes) ||
           Is6To4(bytes);

    private static bool IsIetfProtocolAssignment(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] <= 0x01;

    private static bool IsDocumentation(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;

    private static bool IsDocumentation2(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x3f && (bytes[1] & 0xf0) == 0xf0;

    private static bool Is6To4(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x02;
}
