using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class CollectionScanPolicy
{
    private const string ListTypeName = "System.Collections.Generic.List<T>";

    public static bool TryGetDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        if (method is { IsStatic: false, Name: "TrueForAll" } &&
            string.Equals(typeName, ListTypeName, StringComparison.Ordinal))
        {
            forbidden = "System.Collections.Generic.List.TrueForAll";
            return true;
        }

        forbidden = null!;
        return false;
    }
}
