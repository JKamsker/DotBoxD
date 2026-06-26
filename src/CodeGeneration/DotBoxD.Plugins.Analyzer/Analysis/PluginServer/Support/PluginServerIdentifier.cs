namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

using Microsoft.CodeAnalysis.CSharp;

internal static class PluginServerIdentifier
{
    public static string Escape(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ||
           SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;
}
