using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncSourceIdentifier
{
    public static string Escape(string name)
    {
        var kind = SyntaxFacts.GetKeywordKind(name);
        if (kind == SyntaxKind.None)
        {
            kind = SyntaxFacts.GetContextualKeywordKind(name);
        }

        return kind == SyntaxKind.None ? name : "@" + name;
    }
}
