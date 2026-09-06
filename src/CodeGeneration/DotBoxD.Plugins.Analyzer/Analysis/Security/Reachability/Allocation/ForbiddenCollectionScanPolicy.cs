using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionScanPolicy
{
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IMethodSymbol method, out string forbidden)
    {
        if (method is not { IsStatic: false, MethodKind: MethodKind.Ordinary })
        {
            forbidden = null!;
            return false;
        }

        var typeName = method.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (method.Name is "Clear" or "Contains" or "IndexOf" or "Remove" &&
            string.Equals(typeName, ListTypeName, StringComparison.Ordinal))
        {
            forbidden = $"System.Collections.Generic.List.{method.Name}";
            return true;
        }

        if (method.Name == "TrimExcess" &&
            string.Equals(typeName, StackTypeName, StringComparison.Ordinal))
        {
            forbidden = "System.Collections.Generic.Stack.TrimExcess";
            return true;
        }

        forbidden = null!;
        return false;
    }
}
