using System.Collections.Generic;
using System.Text;

namespace DotBoxD.Services.SourceGenerator.Infrastructure;

internal static class GeneratedWarningPragmaEmitter
{
    public static void AppendDisable(StringBuilder sb, string diagnosticId)
    {
        var ids = JoinPragmaIds(new[] { diagnosticId });
        if (ids.Length > 0)
        {
            sb.Append("#pragma warning disable ").AppendLine(ids);
        }
    }

    public static void AppendDisable(StringBuilder sb, IEnumerable<string> diagnosticIds)
    {
        var ids = JoinPragmaIds(diagnosticIds);
        if (ids.Length > 0)
        {
            sb.Append("#pragma warning disable ").AppendLine(ids);
        }
    }

    public static void AppendRestore(StringBuilder sb, string diagnosticId)
    {
        var ids = JoinPragmaIds(new[] { diagnosticId });
        if (ids.Length > 0)
        {
            sb.Append("#pragma warning restore ").AppendLine(ids);
        }
    }

    public static void AppendRestore(StringBuilder sb, IEnumerable<string> diagnosticIds)
    {
        var ids = JoinPragmaIds(diagnosticIds);
        if (ids.Length > 0)
        {
            sb.Append("#pragma warning restore ").AppendLine(ids);
        }
    }

    private static string JoinPragmaIds(IEnumerable<string> diagnosticIds)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var diagnosticId in diagnosticIds)
        {
            if (!IsPragmaSafe(diagnosticId) || !seen.Add(diagnosticId))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(diagnosticId);
        }

        return sb.ToString();
    }

    private static bool IsPragmaSafe(string diagnosticId)
    {
        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            return false;
        }

        foreach (var c in diagnosticId)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
