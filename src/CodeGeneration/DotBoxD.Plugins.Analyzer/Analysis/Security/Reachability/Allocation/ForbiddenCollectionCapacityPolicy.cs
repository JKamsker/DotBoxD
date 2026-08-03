using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionCapacityPolicy
{
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string ImmutableArrayTypeName = "System.Collections.Immutable.ImmutableArray";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";
    private const string QueueTypeName = "System.Collections.Generic.Queue<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IMethodSymbol? method, out string forbidden)
    {
        forbidden = null!;
        if (method is null || !HasCapacityParameter(method))
        {
            return false;
        }

        var typeName = CapacityTypeName(method);
        return TryGetFactoryDisplayName(method, typeName, out forbidden) ||
               TryGetConstructorDisplayName(method, typeName, out forbidden);
    }

    private static bool TryGetFactoryDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        forbidden = IsImmutableArrayCreateBuilder(method, typeName)
            ? "System.Collections.Immutable.ImmutableArray"
            : null!;
        return forbidden is not null;
    }

    private static bool TryGetConstructorDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        if (method.MethodKind != MethodKind.Constructor)
        {
            forbidden = null!;
            return false;
        }

        var type = method.ContainingType;
        var displayName = ConstructorDisplayName(type, typeName);
        if (displayName is null)
        {
            forbidden = null!;
            return false;
        }

        forbidden = displayName;
        return true;
    }

    private static string? ConstructorDisplayName(INamedTypeSymbol type, string typeName)
        => typeName switch
        {
            ListTypeName => "System.Collections.Generic.List",
            DictionaryTypeName => "System.Collections.Generic.Dictionary",
            QueueTypeName => "System.Collections.Generic.Queue",
            StackTypeName or HashSetTypeName or PriorityQueueTypeName =>
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            _ => null
        };

    private static bool IsImmutableArrayCreateBuilder(IMethodSymbol method, string typeName)
        => method is { IsStatic: true, Name: "CreateBuilder" } &&
           string.Equals(typeName, ImmutableArrayTypeName, StringComparison.Ordinal);

    private static bool HasCapacityParameter(IMethodSymbol method)
    {
        var typeName = CapacityTypeName(method);
        var capacityName =
            typeName is PriorityQueueTypeName or ImmutableArrayTypeName ? "initialCapacity" : "capacity";
        return method.Parameters.Any(parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32 &&
            string.Equals(parameter.Name, capacityName, StringComparison.Ordinal));
    }

    private static string CapacityTypeName(IMethodSymbol method)
        => method.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
}
