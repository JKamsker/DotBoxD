namespace SafeIR.Runtime;

using System.Text.RegularExpressions;

public static partial class AuditTextSanitizer
{
    private const string Redacted = "[redacted]";

    public static string SanitizeAndRedact(string message)
    {
        var chars = message.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        var sanitized = new string(chars);
        sanitized = UriCredentialPattern().Replace(sanitized, "${prefix}[redacted]@");
        sanitized = AuthorizationHeaderPattern().Replace(
            sanitized,
            match => match.Groups["key"].Value +
                     (match.Groups["scheme"].Success ? match.Groups["scheme"].Value + " " : "") +
                     "[redacted]");
        sanitized = SecretPattern().Replace(sanitized, match => match.Groups["key"].Value + "[redacted]");
        return AuthSchemePattern().Replace(
            sanitized,
            match => match.Groups["scheme"].Value + " " + Redacted);
    }

    public static string RedactPathSegments(string path)
    {
        var segments = path.Split('/');
        var previousWasSecretMarker = false;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var isSecret = IsSecretSegment(segment);
            if (previousWasSecretMarker || isSecret)
            {
                segments[i] = Redacted;
            }

            previousWasSecretMarker = isSecret;
        }

        return string.Join("/", segments);
    }

    private static bool IsSecretSegment(string segment)
    {
        var normalized = segment.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return SecretPathSegmentPattern().IsMatch(normalized);
    }

    [GeneratedRegex("(?i)(?<key>\\bauthorization\\s*[:=]\\s*)(?:(?<scheme>bearer|basic)\\s+)?(?<value>[^\\s,;]+)")]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex("(?i)(?<key>\\b(?:password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|session[_-]?token|api[_-]?key|account[_-]?key|client[_-]?secret|private[_-]?key)\\s*[:=]\\s*)(?<value>[^\\s,;]+)")]
    private static partial Regex SecretPattern();

    [GeneratedRegex("(?i)\\b(?<scheme>bearer|basic)\\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex AuthSchemePattern();

    [GeneratedRegex("(?<prefix>\\b[A-Za-z][A-Za-z0-9+.-]*://)[^\\s/@:]+:[^\\s/@]+@")]
    private static partial Regex UriCredentialPattern();

    [GeneratedRegex("(?i)(^|[-_.])(authorization|bearer|credential|key|password|passwd|pwd|secret|session|signature|token)([-_.]|$)")]
    private static partial Regex SecretPathSegmentPattern();
}
