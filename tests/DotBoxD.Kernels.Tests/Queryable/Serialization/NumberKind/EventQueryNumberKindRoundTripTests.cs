using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryNumberKindRoundTripTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(42.0)]
    public void Integral_valued_numbers_preserve_number_kind_through_value_json(double value)
    {
        var restored = RoundTrip(QueryValue.FromNumber(value));

        Assert.Equal(QueryValueKind.Number, restored.Kind);
        Assert.Equal(value, restored.Number);
    }

    [Fact]
    public void Non_integral_numbers_still_preserve_number_kind_through_value_json()
    {
        var restored = RoundTrip(QueryValue.FromNumber(1.5));

        Assert.Equal(QueryValueKind.Number, restored.Kind);
        Assert.Equal(1.5, restored.Number);
    }

    [Fact]
    public void Integral_valued_numbers_preserve_number_kind_inside_document_filters()
    {
        var document = EventQueryDocument.Create(
            "AttackEvent",
            QueryFilter.Compare("Damage", QueryComparisonOperator.Equal, QueryValue.FromNumber(42.0)),
            QueryProjection.Identity);

        var restored = EventQueryJson.Deserialize(EventQueryJson.Serialize(document));

        Assert.NotNull(restored.Filter.Value);
        Assert.Equal(QueryValueKind.Number, restored.Filter.Value.Kind);
        Assert.Equal(42.0, restored.Filter.Value.Number);
    }

    private static QueryValue RoundTrip(QueryValue value) =>
        JsonSerializer.Deserialize<QueryValue>(
            JsonSerializer.Serialize(value, EventQueryJson.Options),
            EventQueryJson.Options)!;
}
