using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionCapacityPolicy
{
    private const string ArrayListTypeName = "System.Collections.ArrayList";
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string ImmutableArrayTypeName = "System.Collections.Immutable.ImmutableArray";
    private const string ImmutableArrayBuilderTypeName =
        "System.Collections.Immutable.ImmutableArray<T>.Builder";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";
    private const string QueueTypeName = "System.Collections.Generic.Queue<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IMethodSymbol? method, out string forbidden)
    {
        if (method is null)
        {
            forbidden = null!;
            return false;
        }

        var typeName = CapacityTypeName(method);
        if (!IsCapacityAllocationMethod(method, typeName))
        {
            forbidden = null!;
            return false;
        }

        if (IsImmutableArrayCreateBuilder(method, typeName))
        {
            forbidden = "System.Collections.Immutable.ImmutableArray";
            return true;
        }

        var type = method.ContainingType;
        var displayName = CollectionDisplayName(type, typeName);
        if (displayName is null)
        {
            forbidden = null!;
            return false;
        }

        forbidden = displayName;
        return true;
    }

    private static string? CollectionDisplayName(INamedTypeSymbol type, string typeName)
        => typeName switch
        {
            ArrayListTypeName => ArrayListTypeName,
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

    public static bool TryGetDisplayName(IPropertySymbol? property, out string forbidden)
    {
        if (property is not
            {
                IsStatic: false,
                Name: "Capacity",
                SetMethod: not null,
                Type.SpecialType: SpecialType.System_Int32
            })
        {
            forbidden = null!;
            return false;
        }

        var typeName = property.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        forbidden = typeName switch
        {
            ImmutableArrayBuilderTypeName => "System.Collections.Immutable.ImmutableArray",
            ListTypeName => "System.Collections.Generic.List",
            _ => null!
        };
        return forbidden is not null;
    }

    private static bool IsCapacityAllocationMethod(IMethodSymbol method, string typeName)
    {
        if (method.MethodKind == MethodKind.Constructor)
        {
            var capacityName = typeName == PriorityQueueTypeName ? "initialCapacity" : "capacity";
            return HasCapacityParameter(method, capacityName);
        }

        if (method.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        if (IsImmutableArrayCreateBuilder(method, typeName))
        {
            return HasCapacityParameter(method, "initialCapacity");
        }

        return string.Equals(typeName, DictionaryTypeName, StringComparison.Ordinal) &&
               string.Equals(method.Name, "EnsureCapacity", StringComparison.Ordinal) &&
               HasCapacityParameter(method, "capacity");
    }

    private static bool HasCapacityParameter(IMethodSymbol method, string capacityName)
        => method.Parameters.Any(parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32 &&
            string.Equals(parameter.Name, capacityName, StringComparison.Ordinal));

    private static string CapacityTypeName(IMethodSymbol method)
        => method.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
}
