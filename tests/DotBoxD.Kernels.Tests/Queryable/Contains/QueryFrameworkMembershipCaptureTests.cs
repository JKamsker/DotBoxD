using System.Collections;
using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryFrameworkMembershipCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "list", "set", "sorted-set", "linked-list", "queue", "stack", "collection", "read-only-collection", "read-only-set", "nested", "read-only-keys", "read-only-values" })
        {
            yield return [kind, false];
            yield return [kind, true];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Capture_preserves_the_selected_membership_implementation(string kind, bool staticContains)
    {
        var values = FrameworkMembershipCollections.Create(kind);
        var predicate = Predicate(values, staticContains);
        var nativeMember = staticContains && kind is "queue" or "stack" ? 2 : 1;
        Assert.True(predicate.Compile()(Event(nativeMember)));

        AssertParity(predicate);
    }

    [Theory]
    [InlineData("generic", false)]
    [InlineData("generic", true)]
    [InlineData("nongeneric", false)]
    [InlineData("nongeneric", true)]
    [InlineData("public", false)]
    [InlineData("public", true)]
    [InlineData("throwing", false)]
    [InlineData("throwing", true)]
    public void Custom_enumerators_do_not_replace_inherited_list_membership(string kind, bool staticContains)
    {
        IEnumerable<int> values = kind switch
        {
            "generic" => new GenericOnlyList(),
            "nongeneric" => new NongenericOnlyList(),
            "public" => new HiddenPublicEnumeratorList(),
            "throwing" => new ThrowingEnumerationList(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        AssertParity(Predicate(values, staticContains));
    }

    [Fact]
    public void Enumerable_only_sources_still_use_their_generic_enumerator()
    {
        IEnumerable<int> values = new SplitEnumerable();

        AssertParity(e => values.Contains(e.Damage));
    }

    [Fact]
    public void Captured_framework_membership_is_a_snapshot()
    {
        var values = new FrameworkMembershipCollections.SplitList();
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => values.Contains(e.Damage));
        values.Clear();
        values.Add(5);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);

        Assert.True(QueryFilterEvaluator.Evaluate(filter, Event(1), reader));
        Assert.True(compiled(Event(1)));
        Assert.False(QueryFilterEvaluator.Evaluate(filter, Event(5), reader));
        Assert.False(compiled(Event(5)));
    }

    private static Expression<Func<AttackTestEvent, bool>> Predicate(IEnumerable<int> values, bool staticContains)
    {
        if (staticContains)
        {
            return e => Enumerable.Contains(values, e.Damage);
        }

        return values switch
        {
            Queue<int> queue => e => queue.Contains(e.Damage),
            Stack<int> stack => e => stack.Contains(e.Damage),
            ICollection<int> collection => e => collection.Contains(e.Damage),
            _ => throw new ArgumentException("An instance membership collection is required.", nameof(values))
        };
    }

    private static void AssertParity(Expression<Func<AttackTestEvent, bool>> predicate)
    {
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        var authored = predicate.Compile();
        foreach (var number in new[] { 0, 1, 2, 3 })
        {
            Assert.Equal(authored(Event(number)), QueryFilterEvaluator.Evaluate(filter, Event(number), reader));
            Assert.Equal(authored(Event(number)), compiled(Event(number)));
        }
    }

    private static AttackTestEvent Event(int damage) => new("alice", "target", damage, 1);

    private sealed class GenericOnlyList() : List<int>([1]), IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => FrameworkMembershipCollections.Generic();
    }

    private sealed class NongenericOnlyList() : List<int>([1]), IEnumerable
    {
        IEnumerator IEnumerable.GetEnumerator() => FrameworkMembershipCollections.Nongeneric();
    }

    private sealed class HiddenPublicEnumeratorList() : List<int>([1])
    {
        public new IEnumerator<int> GetEnumerator() => FrameworkMembershipCollections.Generic();
    }

    private sealed class ThrowingEnumerationList() : List<int>([1]), IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => throw new OperationCanceledException();
        IEnumerator IEnumerable.GetEnumerator() => throw new OperationCanceledException();
    }

    private sealed class SplitEnumerable : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => FrameworkMembershipCollections.Generic();
        IEnumerator IEnumerable.GetEnumerator() => FrameworkMembershipCollections.Nongeneric();
    }
}
