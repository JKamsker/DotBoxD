namespace DotBoxD.Hosting.Http;

internal static class SafeHttpUriAudit
{
    public static string Sanitize(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{NormalizedAuthority(uri)}{SafePath(uri)}"
            : "invalid-uri";

    public static bool MatchesAllowedAuthority(string allowed, Uri uri)
        => MatchesAllowedAuthority(allowed, uri, NormalizedAuthority(uri));

    public static bool MatchesAllowedAuthority(SafeHttpAllowedAuthorityIndex allowedAuthorities, Uri uri)
    {
        if (allowedAuthorities.ContainsExact(uri))
        {
            return true;
        }

        return uri.IsDefaultPort && allowedAuthorities.ContainsExplicitDefault(uri.Scheme, uri.Host);
    }

    public static bool MatchesAllowedAuthority(IReadOnlySet<string> allowedHosts, Uri uri)
    {
        if (allowedHosts.Count == 0)
        {
            return false;
        }

        var authority = NormalizedAuthority(uri);
        foreach (var allowed in allowedHosts)
        {
            if (MatchesAllowedAuthority(allowed, uri, authority))
            {
                return true;
            }
        }

        return false;
    }

    public static bool SameUri(Uri left, Uri right)
        => ReferenceEquals(left, right) ||
           StringComparer.OrdinalIgnoreCase.Equals(left.Scheme, right.Scheme) &&
           SameAuthority(left, right) &&
           StringComparer.Ordinal.Equals(left.PathAndQuery, right.PathAndQuery);

    private static string NormalizedAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    private static bool MatchesAllowedAuthority(string allowed, Uri uri, string authority)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(allowed, authority))
        {
            return true;
        }

        return uri.IsDefaultPort &&
            (StringComparer.OrdinalIgnoreCase.Equals(allowed, uri.Host) ||
             MatchesExplicitDefaultPortAuthority(allowed, uri));
    }

    private static bool MatchesExplicitDefaultPortAuthority(string allowed, Uri uri)
        => TryGetDefaultPort(uri.Scheme, out var defaultPort) &&
           SafeHttpAllowedAuthorityIndex.TryReadAuthorityPort(allowed, out var separator, out var port) &&
           port == defaultPort &&
           allowed.AsSpan(0, separator).Equals(uri.Host, StringComparison.OrdinalIgnoreCase);

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

    private static bool SameAuthority(Uri left, Uri right)
        => left.Port == right.Port &&
           StringComparer.OrdinalIgnoreCase.Equals(left.Host, right.Host);

    private static string SafePath(Uri uri)
        => AuditTextSanitizer.RedactPathSegments(Uri.UnescapeDataString(uri.AbsolutePath));
}
