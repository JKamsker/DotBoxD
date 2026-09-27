using System.Reflection;

namespace DotBoxD.Queryable.Translation;

// These details determine Enumerable.Contains dispatch on the running framework. Match actual
// framework types before reading their layout, and fail closed if a recognized layout changes.
internal static class LinqIteratorReader
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Dictionary<string, IteratorMembership> MembershipKinds = new(StringComparer.Ordinal)
    {
        ["Iterator`1"] = IteratorMembership.Enumeration,
        ["CastICollectionIterator`1"] = IteratorMembership.Enumeration,
        ["OfTypeIterator`1"] = IteratorMembership.Enumeration,
        ["IEnumerableSelectIterator`2"] = IteratorMembership.Enumeration,
        ["ArraySelectIterator`2"] = IteratorMembership.Enumeration,
        ["RangeSelectIterator`2"] = IteratorMembership.Enumeration,
        ["ListSelectIterator`2"] = IteratorMembership.Enumeration,
        ["IListSelectIterator`2"] = IteratorMembership.Enumeration,
        ["IListSkipTakeSelectIterator`2"] = IteratorMembership.Enumeration,
        ["IEnumerableWhereIterator`1"] = IteratorMembership.Enumeration,
        ["ArrayWhereIterator`1"] = IteratorMembership.Enumeration,
        ["ListWhereIterator`1"] = IteratorMembership.Enumeration,
        ["ArrayWhereSelectIterator`2"] = IteratorMembership.Enumeration,
        ["ListWhereSelectIterator`2"] = IteratorMembership.Enumeration,
        ["IEnumerableWhereSelectIterator`2"] = IteratorMembership.Enumeration,
        ["ReverseIterator`1"] = IteratorMembership.Source,
        ["OrderedIterator`1"] = IteratorMembership.Source,
        ["ShuffleIterator`1"] = IteratorMembership.Source,
        ["Concat2Iterator`1"] = IteratorMembership.Concatenation,
        ["ConcatNIterator`1"] = IteratorMembership.Concatenation,
        ["DistinctIterator`1"] = IteratorMembership.Set,
        ["UnionIterator`1"] = IteratorMembership.Set,
        ["AppendPrepend1Iterator`1"] = IteratorMembership.Appended,
        ["AppendPrependN`1"] = IteratorMembership.Appended,
        ["DefaultIfEmptyIterator`1"] = IteratorMembership.Default,
        ["SelectManySingleSelectorIterator`2"] = IteratorMembership.Selected,
        ["ShuffleTakeIterator`1"] = IteratorMembership.ShuffleTake
    };

    public static IteratorMembership MembershipKind(string owner) => MembershipKinds.GetValueOrDefault(owner);

    public static Type? ContainsOwner<T>(IEnumerable<T> source)
    {
        var sizeOptimized = typeof(Enumerable).GetProperty("IsSizeOptimized", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Cannot resolve framework iterator dispatch mode.");
        if (sizeOptimized.GetValue(null) is true)
        {
            return null;
        }

        for (var type = source.GetType(); type is not null; type = type.BaseType)
        {
            if (type.DeclaringType == typeof(Enumerable) && type.Name == "Iterator`1" &&
                type.GetGenericArguments()[0] == typeof(T))
            {
                return source.GetType().GetMethod(nameof(Enumerable.Contains), InstanceFlags, [typeof(T)])?.DeclaringType
                    ?? throw new InvalidOperationException("Cannot resolve framework iterator Contains.");
            }
        }

        return null;
    }

    public static object? Field(object source, string name)
    {
        for (var type = source.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly) is { } field)
            {
                return field.GetValue(source);
            }
        }

        throw new InvalidOperationException($"Cannot read framework iterator field '{name}'.");
    }

    public static IEnumerable<IEnumerable<T>> Sources<T>(object iterator)
    {
        var getSource = iterator.GetType().GetMethod("GetEnumerable", InstanceFlags)!
            .CreateDelegate<Func<int, IEnumerable<T>?>>(iterator);
        for (var index = 0; getSource(index) is { } source; index++)
        {
            yield return source;
        }
    }

    public static IEnumerable<T> LinkedValues<T>(object? node)
    {
        while (node is not null)
        {
            yield return (T)node.GetType().GetProperty("Item")!.GetValue(node)!;
            node = node.GetType().GetProperty("Linked")!.GetValue(node);
        }
    }

    public static IEnumerable<IEnumerable<T>> SelectedSources<T>(object iterator)
    {
        var select = typeof(LinqIteratorReader).GetMethod(nameof(SelectSources), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(iterator.GetType().GetGenericArguments()[0], typeof(T))
            .CreateDelegate<Func<object, IEnumerable<IEnumerable<T>>>>();
        return select(iterator);
    }

    private static IEnumerable<IEnumerable<TResult>> SelectSources<TSource, TResult>(object iterator)
    {
        var source = (IEnumerable<TSource>)Field(iterator, "_source")!;
        var selector = (Func<TSource, IEnumerable<TResult>>)Field(iterator, "_selector")!;
        foreach (var item in source)
        {
            yield return selector(item);
        }
    }
}

internal enum IteratorMembership
{
    Unsupported,
    Enumeration,
    Source,
    Concatenation,
    Set,
    Appended,
    Default,
    Selected,
    ShuffleTake
}
