namespace DotBoxD.Kernels.Runtime;

using System.Text.RegularExpressions;

public static partial class AuditTextSanitizer
{
    private const string Redacted = "[redacted]";
    private const RegexOptions RedactionOptions =
        RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex AuthorizationHeaderRegex = new(
        "(?i)(?<key>\\bauthorization\\s*[:=]\\s*)(?:(?<scheme>bearer|basic)\\s+)?(?<value>[^\\s,;]+)",
        RedactionOptions);

    private static readonly Regex SecretRegex = new(
        "(?i)(?<key>\\b(?:password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|session[_-]?token|api[_-]?key|account[_-]?key|client[_-]?secret|private[_-]?key)\\s*[:=]\\s*)(?<value>[^\\s,;]+)",
        RedactionOptions);

    private static readonly Regex AuthSchemeRegex = new(
        "(?i)\\b(?<scheme>bearer|basic)\\s+[A-Za-z0-9._~+/=-]+",
        RedactionOptions);

    private static readonly Regex UriCredentialRegex = new(
        "(?<prefix>\\b[A-Za-z][A-Za-z0-9+.-]*://)[^\\s/@:]+:[^\\s/@]+@",
        RedactionOptions);

    private static readonly Regex SecretPathSegmentRegex = new(
        "(?i)(^|[-_.])(authorization|bearer|credential|key|password|passwd|pwd|secret|session|signature|token)([-_.]|$)",
        RedactionOptions);

    public static string SanitizeAndRedact(string message)
    {
        if (!RequiresSanitization(message))
        {
            return message;
        }

        var chars = message.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        var sanitized = new string(chars);
        sanitized = UriCredentialRegex.Replace(sanitized, "${prefix}[redacted]@");
        sanitized = AuthorizationHeaderRegex.Replace(
            sanitized,
            match => match.Groups["key"].Value +
                     (match.Groups["scheme"].Success ? match.Groups["scheme"].Value + " " : "") +
                     "[redacted]");
        sanitized = SecretRegex.Replace(sanitized, match => match.Groups["key"].Value + "[redacted]");
        return AuthSchemeRegex.Replace(
            sanitized,
            match => match.Groups["scheme"].Value + " " + Redacted);
    }

    /// <summary>
    /// Cheap, allocation-free prefilter that returns <c>true</c> only when
    /// <see cref="SanitizeAndRedact"/> could observably change <paramref name="message"/>.
    /// It is intentionally conservative: it never returns <c>false</c> for text that any
    /// redaction pass would rewrite. Control characters are sanitized; every redaction
    /// regex requires a credential separator ('@', ':', or '=') or an auth scheme keyword
    /// ("bearer"/"basic"), so the absence of all of these guarantees the message is clean.
    /// </summary>
    private static bool RequiresSanitization(string message)
    {
        foreach (var c in message)
        {
            if (char.IsControl(c) || c is '@' or ':' or '=')
            {
                return true;
            }
        }

        return message.Contains("bearer", StringComparison.OrdinalIgnoreCase)
            || message.Contains("basic", StringComparison.OrdinalIgnoreCase);
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

        return SecretPathSegmentRegex.IsMatch(normalized);
    }
}
