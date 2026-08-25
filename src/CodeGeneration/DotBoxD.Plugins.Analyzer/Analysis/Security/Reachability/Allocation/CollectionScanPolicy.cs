using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class CollectionScanPolicy
{
    private const string ListTypeName = "System.Collections.Generic.List<T>";

    public static bool TryGetDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        forbidden = (method.Name, typeName) switch
        {
            ("RemoveRange", ListTypeName) when !method.IsStatic =>
                "System.Collections.Generic.List.RemoveRange",
            _ => null!
        };
        return forbidden is not null;
    }
}
