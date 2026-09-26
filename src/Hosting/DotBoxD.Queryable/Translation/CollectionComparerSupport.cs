using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reflection;

namespace DotBoxD.Queryable.Translation;

internal static class CollectionComparerSupport
{
    public static bool HasUnsupportedComparer(object collection)
    {
        var comparer = GetComparer(collection);
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

    private static object? GetComparer(object collection)
    {
        HashSet<object>? visited = null;
        while (true)
        {
            var type = collection.GetType();
            // Immutable sets and their builders expose the membership comparer as KeyComparer.
            var propertyName = UsesKeyComparer(type) ? "KeyComparer" : "Comparer";
            var comparer = type.GetProperty(propertyName)?.GetValue(collection);
            if (comparer is not null)
            {
                return comparer;
            }

            var inner = GetWrappedCollection(collection, type) ?? GetKeyCollectionOwner(collection, type);
            if (inner is null)
            {
                return null;
            }

            visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(collection))
            {
                throw new QueryTranslationException("Cannot determine the comparer of a cyclic collection wrapper.");
            }

            collection = inner;
        }
    }

    private static object? GetWrappedCollection(object collection, Type type)
    {
        // These framework wrappers delegate membership to their protected backing collection.
        // Walk base types as inherited Contains implementations keep those same semantics.
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType)
            {
                continue;
            }

            var definition = current.GetGenericTypeDefinition();
            var propertyName = definition == typeof(ReadOnlySet<>) ? "Set" :
                definition == typeof(Collection<>) || definition == typeof(ReadOnlyCollection<>) ? "Items" : null;
            if (propertyName is not null)
            {
                return current.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(collection);
            }
        }

        return null;
    }

    private static object? GetKeyCollectionOwner(object collection, Type type)
    {
        if (type.DeclaringType is not { IsGenericType: true } declaringType)
        {
            return null;
        }

        // Key views preserve their owner's comparer but do not expose it publicly.
        var definition = declaringType.GetGenericTypeDefinition();
        if (string.Equals(type.Name, "KeyList", StringComparison.Ordinal) && definition == typeof(SortedList<,>))
        {
            return GetFieldValue(collection, "_dict");
        }

        if (!string.Equals(type.Name, "KeyCollection", StringComparison.Ordinal) ||
            !IsDictionaryKeyCollection(definition))
        {
            return null;
        }

        return GetFieldValue(collection, "_dictionary") ?? GetFieldValue(collection, "_collection");
    }

    private static object? GetFieldValue(object collection, string fieldName) =>
        collection.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(collection);

    private static bool IsDictionaryKeyCollection(Type type)
        => type == typeof(Dictionary<,>) ||
            type == typeof(SortedDictionary<,>) ||
            type == typeof(ReadOnlyDictionary<,>);

    private static bool UsesKeyComparer(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(ImmutableHashSet<>) ||
               definition == typeof(ImmutableHashSet<>.Builder) ||
               definition == typeof(ImmutableSortedSet<>) ||
               definition == typeof(ImmutableSortedSet<>.Builder);
    }
}
