using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class HashtableDictionaryConstructionPolicy
{
    private const string HashtableTypeName = "System.Collections.Hashtable";
    private const string DictionaryTypeName = "System.Collections.IDictionary";

    public static bool IsMatch(IMethodSymbol method, string typeName)
        => string.Equals(typeName, HashtableTypeName, StringComparison.Ordinal) &&
           method.Parameters.Any(static parameter =>
               string.Equals(
                   parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                   DictionaryTypeName,
                   StringComparison.Ordinal));

    public static string DisplayName(IMethodSymbol method)
        => $"{HashtableTypeName}..ctor({string.Join(", ", method.Parameters.Select(static parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)))})";
}
