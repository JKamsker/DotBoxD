using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerXmlDocumentation
{
    public static string FromSymbol(
        ISymbol symbol,
        string fallbackSummary,
        CancellationToken cancellationToken)
    {
        var xml = symbol.GetDocumentationCommentXml(null, true, cancellationToken);
        var documentation = InnerXml(xml);
        return string.IsNullOrWhiteSpace(documentation)
            ? Summary(fallbackSummary)
            : documentation.Trim();
    }

    public static string Summary(string text)
        => "<summary>" + Escape(text) + "</summary>";

    public static void Append(StringBuilder builder, string indent, string documentation)
    {
        if (string.IsNullOrWhiteSpace(documentation))
        {
            return;
        }

        var lines = documentation
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim()
            .Split('\n');
        foreach (var line in lines)
        {
            builder.Append(indent).Append("/// ").AppendLine(line.Trim());
        }
    }

    public static void AppendSummary(StringBuilder builder, string indent, string text)
        => Append(builder, indent, Summary(text));

    private static string InnerXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return string.Empty;
        }

        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var node in root.Nodes())
            {
                builder.AppendLine(node.ToString(SaveOptions.None));
            }

            return builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Escape(string text)
        => text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}
