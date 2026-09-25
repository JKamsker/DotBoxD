using System.Collections.ObjectModel;
using System.Reflection;

namespace DotBoxD.Queryable.Translation;

internal static class CollectionComparerSupport
{
    public static bool HasUnsupportedComparer(object collection)
    {
        var comparer = GetComparer(collection, depth: 0);
        if (comparer is null || ReferenceEquals(comparer, StringComparer.Ordinal))
        {
            return false;
        }

        // Sample comparisons cannot establish that an arbitrary comparer has portable equality semantics.
        // Recognize the framework defaults by identity without invoking user comparer code.
        return HasCustomGenericComparer(comparer);
    }

    private static bool HasCustomGenericComparer(object comparer)
    {
        foreach (var interfaceType in comparer.GetType().GetInterfaces())
        {
            if (!interfaceType.IsGenericType)
            {
                continue;
            }

            var interfaceDefinition = interfaceType.GetGenericTypeDefinition();
            var elementType = interfaceType.GetGenericArguments()[0];
            if (interfaceDefinition == typeof(IEqualityComparer<>))
            {
                return !IsDefaultComparer(comparer, typeof(EqualityComparer<>), elementType);
            }

            if (interfaceDefinition == typeof(IComparer<>))
            {
                // Default string ordering is culture-sensitive; only StringComparer.Ordinal is portable.
                return elementType == typeof(string) ||
                       !IsDefaultComparer(comparer, typeof(Comparer<>), elementType);
            }
        }

        return false;
    }

    private static bool IsDefaultComparer(object comparer, Type openComparerType, Type elementType)
    {
        var defaultComparer = openComparerType
            .MakeGenericType(elementType)
            .GetProperty(nameof(EqualityComparer<int>.Default))
            ?.GetValue(null);
        return ReferenceEquals(comparer, defaultComparer);
    }

    private static object? GetComparer(object collection, int depth)
    {
        var type = collection.GetType();
        var comparer = type.GetProperty("Comparer")?.GetValue(collection);
        if (comparer is not null)
        {
            return comparer;
        }

        if (depth >= 3)
        {
            return null;
        }

        if (!string.Equals(type.Name, "KeyCollection", StringComparison.Ordinal) ||
            type.DeclaringType is not { IsGenericType: true } declaringType ||
            !IsDictionaryKeyCollection(declaringType.GetGenericTypeDefinition()))
        {
            return null;
        }

        // Dictionary key views preserve their owner's comparer but do not expose it publicly.
        return GetComparerFromField(collection, "_dictionary", depth) ??
            GetComparerFromField(collection, "_collection", depth);
    }

    private static object? GetComparerFromField(object collection, string fieldName, int depth)
    {
        var inner = collection.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(collection);
        return inner is null ? null : GetComparer(inner, depth + 1);
    }

    private static bool IsDictionaryKeyCollection(Type type)
        => type == typeof(Dictionary<,>) ||
            type == typeof(SortedDictionary<,>) ||
            type == typeof(ReadOnlyDictionary<,>);
}
