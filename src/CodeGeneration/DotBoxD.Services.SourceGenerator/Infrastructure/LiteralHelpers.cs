using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DotBoxD.Services.SourceGenerator.Infrastructure;

internal static class LiteralHelpers
{
    private static readonly Dictionary<char, string> s_stringEscapes = new()
    {
        ['\\'] = "\\\\",
        ['"'] = "\\\"",
        ['\a'] = "\\a",
        ['\b'] = "\\b",
        ['\f'] = "\\f",
        ['\v'] = "\\v",
        ['\r'] = "\\r",
        ['\n'] = "\\n",
        ['\u0085'] = "\\u0085",
        ['\u2028'] = "\\u2028",
        ['\u2029'] = "\\u2029",
        ['\t'] = "\\t",
        ['\0'] = "\\0",
    };

    /// <summary>
    /// Escapes a value that will appear inside a regular C# string literal in generated
    /// source.
    /// </summary>
    public static string EscapeStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            AppendEscapedStringCharacter(sb, c);
        }
        return sb.ToString();
    }

    private static void AppendEscapedStringCharacter(StringBuilder builder, char c)
    {
        if (s_stringEscapes.TryGetValue(c, out var escaped))
        {
            builder.Append(escaped);
            return;
        }

        builder.Append(char.IsControl(c) ? UnicodeEscape(c) : c);
    }

    private static string UnicodeEscape(char c)
        => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
}
