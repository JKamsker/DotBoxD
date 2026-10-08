using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DotBoxD.UI.Razor;

internal static class UiRazorDocument
{
    public static string Compile(string text, CancellationToken token)
    {
        if (text.Length > 262_144)
        { throw new NotSupportedException("Safe Razor source exceeds 256 KiB. Split it into IUiComponent compositions."); }
        var statements = new StringBuilder();
        var markup = new StringBuilder();
        var expressions = new UiRazorExpressions();
        foreach (var line in text.Split('\n'))
        {
            token.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (trimmed.StartsWith("@state ", StringComparison.Ordinal))
            { statements.AppendLine(expressions.State(trimmed.Substring(7))); }
            else if (trimmed.StartsWith("@kernel ", StringComparison.Ordinal))
            { statements.AppendLine(expressions.Kernel(trimmed.Substring(8))); }
            else if (trimmed.StartsWith("@resource ", StringComparison.Ordinal))
            { statements.AppendLine(expressions.Resource(trimmed.Substring(10))); }
            else
            { markup.AppendLine(line); }
        }
        XElement root;
        try
        {
            using var reader = XmlReader.Create(new StringReader(markup.ToString().Replace("@key=", "Key=")),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262_144 });
            root = XElement.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException error)
        { throw new NotSupportedException("Use one well-formed Ui primitive root and quoted safe attributes. Raw HTML, Razor directives, @code, JS and attribute splatting are forbidden; put worker C# in a partial class with [UiLocalHandler]/[UiRemoteHandler]. " + error.Message); }
        var nodes = root.DescendantsAndSelf().ToArray();
        if (nodes.Length > 2_000 || nodes.Any(n => n.Ancestors().Count() >= 64))
        { throw new NotSupportedException("Safe Razor exceeds the 2,000-node or 64-level authoring limit. Compose smaller bounded components."); }
        statements.Append("return ").Append(UiRazorElements.Render(root, expressions, token)).Append(';');
        return statements.ToString();
    }
}
