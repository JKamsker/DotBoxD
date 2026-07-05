using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable.Serialization.DocumentShape;

public sealed class EventQueryDocumentInitializerValidationTests
{
    [Theory]
    [InlineData(nameof(EventQueryDocument.Filter))]
    [InlineData(nameof(EventQueryDocument.Projection))]
    public void Public_document_initializer_with_null_subtree_is_rejected_by_json(string property)
        => AssertNullSubtreeRejection(() => EventQueryJson.Serialize(DocumentWithNullSubtree(property)), property);

    [Theory]
    [InlineData(nameof(EventQueryDocument.Filter))]
    [InlineData(nameof(EventQueryDocument.Projection))]
    public void Public_document_initializer_with_null_subtree_is_rejected_by_fingerprint(string property)
        => AssertNullSubtreeRejection(() => QueryFingerprint.Compute(DocumentWithNullSubtree(property)), property);

    private static EventQueryDocument DocumentWithNullSubtree(string property)
        => property switch
        {
            nameof(EventQueryDocument.Filter) => new EventQueryDocument
            {
                EventName = "AttackEvent",
                Filter = null!,
                Projection = QueryProjection.Identity,
            },
            nameof(EventQueryDocument.Projection) => new EventQueryDocument
            {
                EventName = "AttackEvent",
                Filter = QueryFilter.MatchAll,
                Projection = null!,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
        };

    private static void AssertNullSubtreeRejection(Action action, string property)
    {
        var exception = Record.Exception(action);
        Assert.NotNull(exception);
        Assert.True(
            exception is InvalidOperationException or JsonException,
            $"Expected EventQueryDocument validation, got {exception.GetType().Name}: {exception.Message}");
        Assert.Contains("EventQueryDocument", exception.Message, StringComparison.Ordinal);
        Assert.Contains(property, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
