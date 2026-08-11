using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionCapacityPolicy
{
    private const string ArrayListTypeName = "System.Collections.ArrayList";
    private const string BitArrayTypeName = "System.Collections.BitArray";
    private const string CollectionBaseTypeName = "System.Collections.CollectionBase";
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string HashtableTypeName = "System.Collections.Hashtable";
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

        var displayName = CollectionDisplayName(method.ContainingType, typeName);
        if (displayName is null)
        {
            forbidden = null!;
            return false;
        }

        forbidden = displayName;
        return true;
    }

    public static bool TryGetDisplayName(IObjectCreationOperation creation, out string forbidden)
    {
        var method = creation.Constructor;
        if (method is null)
        {
            forbidden = null!;
            return false;
        }

        var typeName = CapacityTypeName(method);
        if (string.Equals(typeName, BitArrayTypeName, StringComparison.Ordinal) &&
            HasCapacityParameter(method, "length") &&
            HasNonZeroLengthArgument(creation))
        {
            forbidden = BitArrayTypeName;
            return true;
        }

        return TryGetDisplayName(method, out forbidden);
    }

    public static bool TryGetDisplayName(IPropertySymbol? property, out string forbidden)
    {
        if (property is not
            {
                IsStatic: false,
                SetMethod: not null,
                Type.SpecialType: SpecialType.System_Int32
            })
        {
            forbidden = null!;
            return false;
        }

        var typeName = property.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        forbidden = (typeName, property.Name) switch
        {
            (ArrayListTypeName, "Capacity") => ArrayListTypeName,
            (BitArrayTypeName, "Length") => BitArrayTypeName,
            (ImmutableArrayBuilderTypeName, "Capacity") => "System.Collections.Immutable.ImmutableArray",
            (ListTypeName, "Capacity") => "System.Collections.Generic.List",
            _ => null!
        };
        return forbidden is not null;
    }

    private static string? CollectionDisplayName(INamedTypeSymbol type, string typeName)
    {
        if (IsGenericCollection(typeName))
        {
            return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        return typeName switch
        {
            ArrayListTypeName => ArrayListTypeName,
            ListTypeName => "System.Collections.Generic.List",
            DictionaryTypeName => "System.Collections.Generic.Dictionary",
            QueueTypeName => "System.Collections.Generic.Queue",
            HashtableTypeName => "System.Collections.Hashtable",
            _ => InheritsCollectionBase(type) ? CollectionBaseTypeName : null
        };
    }

    private static bool IsGenericCollection(string typeName)
        => typeName is StackTypeName or HashSetTypeName or PriorityQueueTypeName;

    private static bool InheritsCollectionBase(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (string.Equals(
                    baseType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    CollectionBaseTypeName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

    private static bool IsImmutableArrayCreateBuilder(IMethodSymbol method, string typeName)
        => method is { IsStatic: true, Name: "CreateBuilder" } &&
           string.Equals(typeName, ImmutableArrayTypeName, StringComparison.Ordinal);

    private static bool HasCapacityParameter(IMethodSymbol method, string capacityName)
        => method.Parameters.Any(parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32 &&
            string.Equals(parameter.Name, capacityName, StringComparison.Ordinal));

    private static bool HasNonZeroLengthArgument(IObjectCreationOperation creation)
        => creation.Arguments.Any(static argument =>
            argument.Parameter is { Type.SpecialType: SpecialType.System_Int32, Name: "length" } &&
            argument.Value.ConstantValue is not { HasValue: true, Value: 0 });

    private static string CapacityTypeName(IMethodSymbol method)
        => method.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
}
