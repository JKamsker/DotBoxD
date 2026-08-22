using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionCapacityPolicy
{
    private const string ArrayBufferWriterTypeName = "System.Buffers.ArrayBufferWriter<T>";
    private const string ArrayTypeName = "System.Array";
    private const string BufferWriterInterfaceTypeName = "System.Buffers.IBufferWriter<T>";
    private const string ArrayListTypeName = "System.Collections.ArrayList";
    private const string BitArrayTypeName = "System.Collections.BitArray";
    private const string CollectionBaseTypeName = "System.Collections.CollectionBase";
    private const string ConcurrentDictionaryTypeName =
        "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>";
    private const string CollectionsUtilTypeName = "System.Collections.Specialized.CollectionsUtil";
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string HashtableTypeName = "System.Collections.Hashtable";
    private const string HybridDictionaryTypeName = "System.Collections.Specialized.HybridDictionary";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string LinkedListTypeName = "System.Collections.Generic.LinkedList<T>";
    private const string NameObjectCollectionBaseTypeName =
        "System.Collections.Specialized.NameObjectCollectionBase";
    private const string NameValueCollectionTypeName =
        "System.Collections.Specialized.NameValueCollection";
    private const string OrderedDictionaryTypeName = "System.Collections.Specialized.OrderedDictionary";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";
    private const string NonGenericQueueTypeName = "System.Collections.Queue";
    private const string QueueTypeName = "System.Collections.Generic.Queue<T>";
    private const string NonGenericSortedListTypeName = "System.Collections.SortedList";
    private const string SortedListTypeName = "System.Collections.Generic.SortedList<TKey, TValue>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    private static readonly Dictionary<string, string> FixedDisplayNames = new(StringComparer.Ordinal)
    {
        [ArrayListTypeName] = ArrayListTypeName,
        [ConcurrentDictionaryTypeName] = "System.Collections.Concurrent.ConcurrentDictionary",
        [DictionaryTypeName] = "System.Collections.Generic.Dictionary",
        [HashtableTypeName] = "System.Collections.Hashtable",
        [HybridDictionaryTypeName] = HybridDictionaryTypeName,
        [ListTypeName] = "System.Collections.Generic.List",
        [LinkedListTypeName] = "System.Collections.Generic.LinkedList",
        [NameValueCollectionTypeName] = NameValueCollectionTypeName,
        [NonGenericQueueTypeName] = NonGenericQueueTypeName,
        [NonGenericSortedListTypeName] = NonGenericSortedListTypeName,
        [OrderedDictionaryTypeName] = OrderedDictionaryTypeName,
        [QueueTypeName] = "System.Collections.Generic.Queue",
        [SortedListTypeName] = "System.Collections.Generic.SortedList"
    };
    public static bool TryGetDisplayName(IMethodSymbol? method, out string forbidden)
    {
        if (method is null)
        {
            forbidden = null!;
            return false;
        }

        var typeName = CapacityTypeName(method);
        if (IsArrayResize(method, typeName))
        {
            forbidden = ArrayTypeName;
            return true;
        }

        if (CollectionMaterializationPolicy.TryGetDisplayName(method, typeName, out forbidden) ||
            CollectionBulkGrowthPolicy.TryGetDisplayName(method, typeName, out forbidden))
        {
            return true;
        }

        if (IsCollectionsUtilHashtableFactory(method, typeName))
        {
            forbidden = HashtableTypeName;
            return true;
        }

        if (IsHashtableDictionaryConstructor(method, typeName))
        {
            forbidden = HashtableDictionaryConstructorDisplayName(method);
            return true;
        }

        return TryGetCapacityDisplayName(method, typeName, out forbidden);
    }

    private static bool TryGetCapacityDisplayName(
        IMethodSymbol method,
        string typeName,
        out string forbidden)
    {
        if (!IsCapacityAllocationMethod(method, typeName))
        {
            forbidden = null!;
            return false;
        }

        if (ImmutableArrayCapacityPolicy.TryGetDisplayName(method, typeName, out forbidden))
        {
            return true;
        }

        if (IsArrayBufferWriterGrowthHint(method, typeName))
        {
            forbidden = method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return true;
        }

        forbidden = CollectionDisplayName(method.ContainingType, typeName)!;
        return forbidden is not null;
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

        if (string.Equals(typeName, OrderedDictionaryTypeName, StringComparison.Ordinal) &&
            !HasNonEmptyInitializer(creation))
        {
            forbidden = null!;
            return false;
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
        if (ImmutableArrayCapacityPolicy.TryGetPropertyDisplayName(typeName, property.Name, out forbidden))
        {
            return true;
        }

        forbidden = (typeName, property.Name) switch
        {
            (ArrayListTypeName, "Capacity") => ArrayListTypeName,
            (BitArrayTypeName, "Length") => BitArrayTypeName,
            (ListTypeName, "Capacity") => "System.Collections.Generic.List",
            (NonGenericSortedListTypeName, "Capacity") => NonGenericSortedListTypeName,
            (SortedListTypeName, "Capacity") => "System.Collections.Generic.SortedList",
            _ => null!
        };
        return forbidden is not null;
    }

    private static string? CollectionDisplayName(INamedTypeSymbol type, string typeName)
    {
        if (FixedDisplayNames.TryGetValue(typeName, out var displayName))
        {
            return displayName;
        }

        if (UsesOriginalTypeDisplayName(typeName))
        {
            return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        if (InheritsCollectionBase(type, NameObjectCollectionBaseTypeName))
        {
            return NameObjectCollectionBaseTypeName;
        }

        if (InheritsCollectionBase(type, CollectionBaseTypeName))
        {
            return CollectionBaseTypeName;
        }

        return typeName == ArrayBufferWriterTypeName
            ? type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            : null;
    }

    private static bool UsesOriginalTypeDisplayName(string typeName)
        => typeName is StackTypeName or HashSetTypeName or PriorityQueueTypeName;

    private static bool InheritsCollectionBase(INamedTypeSymbol type, string baseTypeName)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (string.Equals(
                    baseType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    baseTypeName,
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
            return HasConstructorCapacityParameter(method, typeName) ||
                   IsLinkedListEnumerableConstructor(method, typeName);
        }

        return method.MethodKind == MethodKind.Ordinary &&
               IsCapacityAllocationOrdinaryMethod(method, typeName);
    }

    private static bool IsLinkedListEnumerableConstructor(IMethodSymbol method, string typeName)
        => string.Equals(typeName, LinkedListTypeName, StringComparison.Ordinal) &&
           method.Parameters.Length == 1 &&
           string.Equals(method.Parameters[0].Name, "collection", StringComparison.Ordinal);

    private static bool IsHashtableDictionaryConstructor(IMethodSymbol method, string typeName)
        => string.Equals(typeName, HashtableTypeName, StringComparison.Ordinal) &&
           method.Parameters.Any(static parameter =>
               string.Equals(
                   parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                   "System.Collections.IDictionary",
                   StringComparison.Ordinal));

    private static string HashtableDictionaryConstructorDisplayName(IMethodSymbol method)
        => $"{HashtableTypeName}..ctor({string.Join(", ", method.Parameters.Select(static parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)))})";

    private static bool HasConstructorCapacityParameter(IMethodSymbol method, string typeName)
    {
        var capacityName = typeName switch
        {
            PriorityQueueTypeName or NonGenericSortedListTypeName or ArrayBufferWriterTypeName =>
                "initialCapacity",
            HybridDictionaryTypeName => "initialSize",
            _ => "capacity"
        };
        return HasCapacityParameter(method, capacityName);
    }

    private static bool IsCapacityAllocationOrdinaryMethod(IMethodSymbol method, string typeName)
    {
        if (ImmutableArrayCapacityPolicy.IsCapacityAllocationMethod(method, typeName))
        {
            return true;
        }

        if (IsArrayBufferWriterGrowthHint(method, typeName))
        {
            return true;
        }

        return typeName is DictionaryTypeName or HashSetTypeName or PriorityQueueTypeName or QueueTypeName or StackTypeName &&
               string.Equals(method.Name, "EnsureCapacity", StringComparison.Ordinal) &&
               HasCapacityParameter(method, "capacity");
    }

    private static bool IsCollectionsUtilHashtableFactory(IMethodSymbol method, string typeName)
        => method is { IsStatic: true, Name: "CreateCaseInsensitiveHashtable" } &&
           string.Equals(typeName, CollectionsUtilTypeName, StringComparison.Ordinal) &&
           HasCapacityParameter(method, "capacity");

    private static bool IsArrayResize(IMethodSymbol method, string typeName)
        => method is { IsStatic: true, Name: "Resize" } &&
           string.Equals(typeName, ArrayTypeName, StringComparison.Ordinal) &&
           HasCapacityParameter(method, "newSize");

    private static bool IsArrayBufferWriterGrowthHint(IMethodSymbol method, string typeName)
        => method.Name is "GetMemory" or "GetSpan" &&
           typeName is ArrayBufferWriterTypeName or BufferWriterInterfaceTypeName &&
           HasCapacityParameter(method, "sizeHint");

    private static bool HasCapacityParameter(IMethodSymbol method, string capacityName)
        => method.Parameters.Any(parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32 &&
            string.Equals(parameter.Name, capacityName, StringComparison.Ordinal));

    private static bool HasNonZeroLengthArgument(IObjectCreationOperation creation)
        => creation.Arguments.Any(static argument =>
            argument.Parameter is { Type.SpecialType: SpecialType.System_Int32, Name: "length" } &&
            argument.Value.ConstantValue is not { HasValue: true, Value: 0 });

    private static bool HasNonEmptyInitializer(IObjectCreationOperation creation)
        => creation.Initializer?.Initializers.Any() == true;

    private static string CapacityTypeName(IMethodSymbol method)
        => method.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
}
