using System.Collections.ObjectModel;

namespace DotBoxD.Kernels.Tests.Queryable;

internal static class HiddenComparerCollections
{
    public static ICollection<string> Create(string kind, ComparerState state) => kind switch
    {
        "hash-set" => new HiddenHashSet(state) { "alice" },
        "sorted-set" => new HiddenSortedSet(state) { "alice" },
        "dictionary-keys" => new HiddenDictionary(state) { ["alice"] = 1 }.Keys,
        "sorted-dictionary-keys" => new HiddenSortedDictionary(state) { ["alice"] = 1 }.Keys,
        "sorted-list-keys" => new HiddenSortedList(state) { ["alice"] = 1 }.Keys,
        "collection" => new HiddenCollection(state),
        "read-only-collection" => new HiddenReadOnlyCollection(state),
        "read-only-set" => new HiddenReadOnlySet(state),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public sealed class ComparerState(bool custom, bool throwing)
    {
        public int Reads { get; private set; }
        public StringComparer Actual => custom ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public StringComparer Exposed
        {
            get
            {
                Reads++;
                return throwing ? throw new InvalidOperationException("Unrelated comparer getter.") :
                    custom ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            }
        }
    }

    public sealed class HiddenHashSet(ComparerState state) : HashSet<string>(state.Actual)
    {
        public new IEqualityComparer<string> Comparer => state.Exposed;
    }

    public sealed class HiddenList(ComparerState state) : List<string>(["alice"])
    {
        public StringComparer Comparer => state.Exposed;
    }

    private sealed class HiddenSortedSet(ComparerState state) : SortedSet<string>(state.Actual)
    {
        public new IComparer<string> Comparer => state.Exposed;
    }

    private sealed class HiddenDictionary(ComparerState state) : Dictionary<string, int>(state.Actual)
    {
        public new IEqualityComparer<string> Comparer => state.Exposed;
    }

    private sealed class HiddenSortedDictionary(ComparerState state) : SortedDictionary<string, int>(state.Actual)
    {
        public new IComparer<string> Comparer => state.Exposed;
    }

    private sealed class HiddenSortedList(ComparerState state) : SortedList<string, int>(state.Actual)
    {
        public new IComparer<string> Comparer => state.Exposed;
    }

    private sealed class HiddenCollection(ComparerState state) : Collection<string>(Keys(state))
    {
        public StringComparer Comparer => state.Exposed;
    }

    private sealed class HiddenReadOnlyCollection(ComparerState state) : ReadOnlyCollection<string>(Keys(state))
    {
        public StringComparer Comparer => state.Exposed;
    }

    private sealed class HiddenReadOnlySet(ComparerState state) : ReadOnlySet<string>(new HashSet<string>(state.Actual) { "alice" })
    {
        public StringComparer Comparer => state.Exposed;
    }

    private static IList<string> Keys(ComparerState state) => new SortedList<string, int>(state.Actual) { ["alice"] = 1 }.Keys;
}
