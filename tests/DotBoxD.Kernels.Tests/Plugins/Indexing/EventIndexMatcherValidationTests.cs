using DotBoxD.Plugins.Indexing;

namespace DotBoxD.Kernels.Tests.Plugins.Indexing;

public sealed class EventIndexMatcherValidationTests
{
    private sealed record IndexedSample([property: EventIndexKey] string Name);

    [Fact]
    public void Create_rejects_null_predicate_entries_at_public_boundary()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => EventIndexMatcher<IndexedSample>.Create([null!]));

        Assert.Equal("predicates", exception.ParamName);
        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
