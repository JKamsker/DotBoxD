using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionScanPolicy
{
    private const string ListTypeName = "System.Collections.Generic.List<T>";

    public static bool TryGetDisplayName(IMethodSymbol method, out string forbidden)
    {
        if (method is { IsStatic: false, MethodKind: MethodKind.Ordinary } &&
            method.Name is "BinarySearch" or "Contains" or "IndexOf" &&
            string.Equals(
                method.ContainingType.OriginalDefinition.ToDisplayString(
                    SymbolDisplayFormat.CSharpErrorMessageFormat),
                ListTypeName,
                StringComparison.Ordinal))
        {
            forbidden = $"System.Collections.Generic.List.{method.Name}";
            return true;
        }

        forbidden = null!;
        return false;
    }
}
