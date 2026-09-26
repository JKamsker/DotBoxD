using System.Collections.ObjectModel;
using System.Reflection;

namespace DotBoxD.Queryable.Translation;

internal static class CollectionWrapperReader
{
    public static object? Read(object collection, Type type)
    {
        // Read the framework declaration, so a derived class cannot replace the backing accessor.
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType)
            {
                continue;
            }

            var definition = current.GetGenericTypeDefinition();
            if (definition == typeof(ReadOnlyDictionary<,>.KeyCollection) ||
                definition == typeof(ReadOnlyDictionary<,>.ValueCollection))
            {
                return current.GetField("_collection", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(collection)
                    ?? throw new InvalidOperationException($"Cannot inspect the backing collection of '{current}'.");
            }

            var propertyName = definition == typeof(ReadOnlySet<>) ? "Set" :
                definition == typeof(Collection<>) || definition == typeof(ReadOnlyCollection<>) ? "Items" : null;
            if (propertyName is not null)
            {
                return current.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(collection)
                    ?? throw new InvalidOperationException($"Cannot inspect the backing collection of '{current}'.");
            }
        }

        return null;
    }
}
