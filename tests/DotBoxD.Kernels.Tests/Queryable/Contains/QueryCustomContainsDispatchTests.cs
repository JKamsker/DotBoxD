using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryCustomContainsDispatchTests
{
    [Theory]
    [InlineData("list", false)]
    [InlineData("list", true)]
    [InlineData("set", false)]
    [InlineData("set", true)]
    [InlineData("virtual-set", false)]
    [InlineData("virtual-set", true)]
    [InlineData("collection", false)]
    [InlineData("collection", true)]
    [InlineData("read-only-collection", false)]
    [InlineData("read-only-collection", true)]
    [InlineData("read-only-set", false)]
    [InlineData("read-only-set", true)]
    [InlineData("derived", false)]
    [InlineData("derived", true)]
    [InlineData("nested", false)]
    [InlineData("nested", true)]
    [InlineData("read-only-keys", false)]
    [InlineData("read-only-keys", true)]
    [InlineData("read-only-values", false)]
    [InlineData("read-only-values", true)]
    public void Custom_membership_through_interfaces_and_wrappers_is_rejected(string kind, bool staticContains)
    {
        var values = CreateCustom(kind);
        Assert.Equal(1, Assert.Single(values));
        Assert.False(values.Contains(1));
        Assert.True(values.Contains(2));
        var staticContainsOne = Enumerable.Contains(values, 1);
        var staticContainsTwo = Enumerable.Contains(values, 2);
        Assert.False(staticContainsOne);
        Assert.True(staticContainsTwo);

        AssertRejected(Predicate(values, staticContains));
    }

    [Fact]
    public void Custom_read_only_set_interface_membership_is_rejected()
    {
        IReadOnlySet<int> values = new CustomSet();
        Assert.Equal(1, Assert.Single(values));
        Assert.True(values.Contains(2));

        AssertRejected(e => values.Contains(e.Damage));
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("read-only-collection")]
    [InlineData("virtual-set")]
    public void Framework_declared_calls_that_dispatch_to_custom_membership_are_rejected(string kind)
    {
        var values = CreateCustom(kind);
        Expression<Func<AttackTestEvent, bool>> predicate = kind switch
        {
            "collection" => e => ((Collection<int>)values).Contains(e.Damage),
            "read-only-collection" => e => ((ReadOnlyCollection<int>)values).Contains(e.Damage),
            "virtual-set" => e => ((SortedSet<int>)values).Contains(e.Damage),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.True(predicate.Compile()(Event(2)));

        AssertRejected(predicate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_array_snapshots_offer_enumeration_membership(bool staticContains)
    {
        ICollection<int> values = CreateCustom("nested").ToArray();

        AssertParity(Predicate(values, staticContains));
        Assert.True(values.Contains(1));
        Assert.False(values.Contains(2));
    }

    [Theory]
    [InlineData("array", false)]
    [InlineData("array", true)]
    [InlineData("list", false)]
    [InlineData("list", true)]
    [InlineData("set", false)]
    [InlineData("set", true)]
    [InlineData("sorted-set", false)]
    [InlineData("sorted-set", true)]
    [InlineData("immutable-set", false)]
    [InlineData("immutable-set", true)]
    [InlineData("immutable-builder", false)]
    [InlineData("immutable-builder", true)]
    [InlineData("read-only-set", false)]
    [InlineData("read-only-set", true)]
    [InlineData("read-only-keys", false)]
    [InlineData("read-only-keys", true)]
    [InlineData("read-only-values", false)]
    [InlineData("read-only-values", true)]
    [InlineData("nested", false)]
    [InlineData("nested", true)]
    public void Framework_membership_implementations_remain_supported(string kind, bool staticContains)
    {
        ICollection<int> values = kind switch
        {
            "array" => new[] { 1, 3 },
            "list" => new List<int> { 1, 3 },
            "set" => new HashSet<int> { 1, 3 },
            "sorted-set" => new SortedSet<int> { 1, 3 },
            "immutable-set" => ImmutableHashSet.Create(1, 3),
            "immutable-builder" => ImmutableSortedSet.Create(1, 3).ToBuilder(),
            "read-only-set" => new ReadOnlySet<int>(new HashSet<int> { 1, 3 }),
            "read-only-keys" => new ReadOnlyDictionary<int, string>(new Dictionary<int, string> { [1] = "one", [3] = "three" }).Keys,
            "read-only-values" => new ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["one"] = 1, ["three"] = 3 }).Values,
            "nested" => new Collection<int>(new ReadOnlyCollection<int>(new List<int> { 1, 3 })),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        AssertParity(Predicate(values, staticContains));
    }

    [Fact]
    public void Nonvirtual_base_calls_preserve_their_own_membership()
    {
        var values = new CustomList();
        Assert.False(((ICollection<int>)values).Contains(1));
        var baseContains = values.Contains(1);
        Assert.True(baseContains);

        AssertParity(e => values.Contains(e.Damage));
    }

    [Fact]
    public void Nonvirtual_set_calls_preserve_their_own_membership()
    {
        var values = new CustomSet();
        Assert.False(((ICollection<int>)values).Contains(1));
        var baseContains = values.Contains(1);
        Assert.True(baseContains);

        AssertParity(e => values.Contains(e.Damage));
    }

    [Fact]
    public void Hidden_methods_do_not_replace_virtual_base_slots()
    {
        var values = new HiddenVirtualSet();
        var hiddenContains = values.Contains(2);
        Assert.True(hiddenContains);
        SortedSet<int> baseValues = values;

        AssertParity(e => baseValues.Contains(e.Damage));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_only_set_interfaces_keep_framework_membership(bool wrapped)
    {
        IReadOnlySet<int> values = wrapped
            ? new ReadOnlySet<int>(new HashSet<int> { 1, 3 })
            : new HashSet<int> { 1, 3 };

        AssertParity(e => values.Contains(e.Damage));
    }

    private static void AssertRejected(Expression<Func<AttackTestEvent, bool>> predicate)
    {
        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("Contains", error.Message, StringComparison.Ordinal);
        Assert.Contains("ToArray", error.Message, StringComparison.Ordinal);
    }

    private static Expression<Func<AttackTestEvent, bool>> Predicate(ICollection<int> values, bool staticContains) =>
        staticContains ? e => Enumerable.Contains(values, e.Damage) : e => values.Contains(e.Damage);

    private static void AssertParity(Expression<Func<AttackTestEvent, bool>> predicate)
    {
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        Assert.Equal(QueryFilterKind.In, filter.Kind);
        var reader = new MemberValueReader();
        var authored = predicate.Compile();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var damage in new[] { 0, 1, 2, 3 })
        {
            var value = Event(damage);
            Assert.Equal(authored(value), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(authored(value), compiled(value));
        }
    }

    private static AttackTestEvent Event(int damage) => new("alice", "target", damage, 1);

    private static ICollection<int> CreateCustom(string kind) => kind switch
    {
        "list" => new CustomList(),
        "set" => new CustomSet(),
        "virtual-set" => new CustomVirtualSet(),
        "collection" => new Collection<int>(new CustomList()),
        "read-only-collection" => new ReadOnlyCollection<int>(new CustomList()),
        "read-only-set" => new ReadOnlySet<int>(new CustomSet()),
        "derived" => new DerivedReadOnlyCollection(new CustomList()),
        "nested" => new Collection<int>(new ReadOnlyCollection<int>(new CustomList())),
        "read-only-keys" => new ReadOnlyDictionary<int, int>(new CustomDictionary()).Keys,
        "read-only-values" => new ReadOnlyDictionary<int, int>(new CustomDictionary()).Values,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class CustomList() : List<int>([1]), ICollection<int>
    {
        bool ICollection<int>.Contains(int item) => item == 2;
    }

    private sealed class CustomSet() : HashSet<int>([1]), ISet<int>, IReadOnlySet<int>
    {
        bool ICollection<int>.Contains(int item) => item == 2;
        bool IReadOnlySet<int>.Contains(int item) => item == 2;
    }

    private sealed class CustomVirtualSet() : SortedSet<int>([1])
    {
        public override bool Contains(int item) => item == 2;
    }

    private sealed class HiddenVirtualSet() : SortedSet<int>([1])
    {
        public new bool Contains(int item) => item == 2;
    }

    private sealed class CustomDictionary : Dictionary<int, int>, IDictionary<int, int>
    {
        ICollection<int> IDictionary<int, int>.Keys => new CustomList();
        ICollection<int> IDictionary<int, int>.Values => new CustomList();
    }

    private sealed class DerivedReadOnlyCollection(IList<int> values) : ReadOnlyCollection<int>(values);
}
