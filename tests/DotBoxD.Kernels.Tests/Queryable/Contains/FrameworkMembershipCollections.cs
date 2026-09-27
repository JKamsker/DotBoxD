using System.Collections;
using System.Collections.ObjectModel;

namespace DotBoxD.Kernels.Tests.Queryable;

internal static class FrameworkMembershipCollections
{
    public static IEnumerable<int> Create(string kind) => kind switch
    {
        "list" => new SplitList(),
        "set" => new SplitSet(),
        "sorted-set" => new SplitSortedSet(),
        "linked-list" => new SplitLinkedList(),
        "queue" => new SplitQueue(),
        "stack" => new SplitStack(),
        "collection" => new Collection<int>(new SplitList()),
        "read-only-collection" => new ReadOnlyCollection<int>(new SplitList()),
        "read-only-set" => new ReadOnlySet<int>(new SplitSet()),
        "nested" => new Collection<int>(new ReadOnlyCollection<int>(new SplitList())),
        "read-only-keys" => new ReadOnlyDictionary<int, int>(new SplitDictionary()).Keys,
        "read-only-values" => new ReadOnlyDictionary<int, int>(new SplitDictionary()).Values,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static IEnumerator<int> Generic() => ((IEnumerable<int>)new[] { 2 }).GetEnumerator();
    public static IEnumerator Nongeneric() => new[] { 3 }.GetEnumerator();

    public sealed class SplitList() : List<int>([1]), IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => Generic();
        IEnumerator IEnumerable.GetEnumerator() => Nongeneric();
    }

    private sealed class SplitSet() : HashSet<int>([1]), IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => Generic();
        IEnumerator IEnumerable.GetEnumerator() => Nongeneric();
    }

    private sealed class SplitSortedSet() : SortedSet<int>([1]), IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => Generic();
        IEnumerator IEnumerable.GetEnumerator() => Nongeneric();
    }

    private sealed class SplitLinkedList() : LinkedList<int>([1]), IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => Generic();
        IEnumerator IEnumerable.GetEnumerator() => Nongeneric();
    }

    private sealed class SplitQueue() : Queue<int>([1]), IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => Generic();
        IEnumerator IEnumerable.GetEnumerator() => Nongeneric();
    }

    private sealed class SplitStack() : Stack<int>([1]), IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => Generic();
        IEnumerator IEnumerable.GetEnumerator() => Nongeneric();
    }

    private sealed class SplitDictionary : Dictionary<int, int>, IDictionary<int, int>
    {
        ICollection<int> IDictionary<int, int>.Keys => new SplitList();
        ICollection<int> IDictionary<int, int>.Values => new SplitList();
    }
}
