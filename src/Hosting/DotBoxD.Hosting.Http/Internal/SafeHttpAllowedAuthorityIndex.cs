using System.Globalization;

namespace DotBoxD.Hosting.Http;

internal sealed class SafeHttpAllowedAuthorityIndex
{
    private readonly HashSet<string> _exactAuthorities;
    private readonly HashSet<HostPort>? _canonicalPortAuthorities;
    private readonly HashSet<string>? _httpDefaultHosts;
    private readonly HashSet<string>? _httpsDefaultHosts;

    public SafeHttpAllowedAuthorityIndex(IEnumerable<string> authorities)
    {
        _exactAuthorities = new HashSet<string>(authorities, StringComparer.OrdinalIgnoreCase);

        foreach (var authority in _exactAuthorities)
        {
            if (!TryReadAuthorityPort(authority, out var separator, out var port) ||
                port < 0)
            {
                continue;
            }

            var host = authority[..separator];
            if (authority.AsSpan(separator + 1).SequenceEqual(port.ToString(CultureInfo.InvariantCulture)))
            {
                (_canonicalPortAuthorities ??= new HashSet<HostPort>(HostPortComparer.Instance))
                    .Add(new HostPort(host, port));
            }

            if (port is 80 or 443)
            {
                var hosts = port == 80
                    ? _httpDefaultHosts ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : _httpsDefaultHosts ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                hosts.Add(host);
            }
        }
    }

    public bool ContainsExact(Uri uri)
        => uri.IsDefaultPort
            ? _exactAuthorities.Contains(uri.Host)
            : _canonicalPortAuthorities is not null &&
              _canonicalPortAuthorities.Contains(new HostPort(uri.Host, uri.Port));

    public bool ContainsExplicitDefault(string scheme, string host)
    {
        var hosts = StringComparer.OrdinalIgnoreCase.Equals(scheme, Uri.UriSchemeHttps)
            ? _httpsDefaultHosts
            : StringComparer.OrdinalIgnoreCase.Equals(scheme, Uri.UriSchemeHttp)
                ? _httpDefaultHosts
                : null;
        return hosts is not null && hosts.Contains(host);
    }

    public static bool TryReadAuthorityPort(string authority, out int separator, out int port)
    {
        separator = authority.StartsWith("[", StringComparison.Ordinal)
            ? authority.IndexOf("]:", StringComparison.Ordinal) + 1
            : authority.LastIndexOf(':');
        if (separator <= 0 || separator == authority.Length - 1)
        {
            port = 0;
            return false;
        }

        return int.TryParse(
            authority.AsSpan(separator + 1),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out port);
    }

    private readonly record struct HostPort(string Host, int Port);

    private sealed class HostPortComparer : IEqualityComparer<HostPort>
    {
        public static HostPortComparer Instance { get; } = new();

        public bool Equals(HostPort x, HostPort y)
            => x.Port == y.Port && StringComparer.OrdinalIgnoreCase.Equals(x.Host, y.Host);

        public int GetHashCode(HostPort value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Host), value.Port);
    }
}
