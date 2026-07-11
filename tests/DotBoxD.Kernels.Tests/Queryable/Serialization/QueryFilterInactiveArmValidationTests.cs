using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryFilterInactiveArmValidationTests
{
    [Fact]
    public void MatchAll_filter_initializer_with_inactive_arms_is_rejected_on_write()
    {
        var filter = new QueryFilter
        {
            Kind = QueryFilterKind.MatchAll,
            Field = "Damage",
            Operator = QueryComparisonOperator.Equal,
            Value = QueryValue.FromString("hidden"),
            Values = [QueryValue.FromInteger(1)],
            Children = [QueryFilter.Compare("Damage", QueryComparisonOperator.Equal, QueryValue.FromInteger(5))],
            IgnoreCase = true,
        };

        AssertInactiveArmRejection(() => JsonSerializer.Serialize(filter, EventQueryJson.Options));
    }

    [Fact]
    public void MatchAll_filter_initializer_with_default_operator_arm_is_rejected_on_write()
    {
        var filter = new QueryFilter
        {
            Kind = QueryFilterKind.MatchAll,
            Operator = QueryComparisonOperator.Equal,
        };

        AssertInactiveArmRejection(() => JsonSerializer.Serialize(filter, EventQueryJson.Options));
    }

    [Fact]
    public void Compare_filter_initializer_with_inactive_arms_is_rejected_on_write()
    {
        var filter = new QueryFilter
        {
            Kind = QueryFilterKind.Compare,
            Field = "Damage",
            Operator = QueryComparisonOperator.Equal,
            Value = QueryValue.FromInteger(5),
            Values = [QueryValue.FromInteger(7)],
            Children = [QueryFilter.MatchAll],
        };

        AssertInactiveArmRejection(() => JsonSerializer.Serialize(filter, EventQueryJson.Options));
    }

    [Fact]
    public void In_filter_initializer_with_inactive_arms_is_rejected_on_write()
    {
        var filter = new QueryFilter
        {
            Kind = QueryFilterKind.In,
            Field = "Damage",
            Value = QueryValue.FromInteger(5),
            Values = [QueryValue.FromInteger(7)],
            Children = [QueryFilter.MatchAll],
        };

        AssertInactiveArmRejection(() => JsonSerializer.Serialize(filter, EventQueryJson.Options));
    }

    [Fact]
    public void Document_serialization_rejects_filter_inactive_arms()
    {
        var document = new EventQueryDocument
        {
            EventName = "AttackEvent",
            Filter = new QueryFilter
            {
                Kind = QueryFilterKind.MatchAll,
                Value = QueryValue.FromString("hidden"),
                Values = [QueryValue.FromInteger(1)],
                Children = [QueryFilter.Compare("Damage", QueryComparisonOperator.Equal, QueryValue.FromInteger(5))],
            },
            Projection = QueryProjection.Identity,
        };

        AssertInactiveArmRejection(() => EventQueryJson.Serialize(document));
    }

    [Theory]
    [InlineData("""{"kind":"all","value":"hidden"}""")]
    [InlineData("""{"kind":"all","op":"eq"}""")]
    [InlineData("""{"kind":"compare","path":"Damage","op":"eq","value":5,"values":[7]}""")]
    [InlineData("""{"kind":"in","path":"Damage","values":[7],"term":{"kind":"all"}}""")]
    public void Filter_json_with_inactive_arms_is_rejected_on_read(string json)
        => AssertInactiveArmRejection(() =>
            JsonSerializer.Deserialize<QueryFilter>(json, EventQueryJson.Options));

    private static void AssertInactiveArmRejection(Action action)
    {
        var exception = Record.Exception(action);
        Assert.NotNull(exception);
        Assert.True(
            exception is InvalidOperationException or JsonException,
            $"Expected filter inactive-arm validation, got {exception.GetType().Name}: {exception.Message}");
        Assert.Contains("QueryFilter", exception.Message, StringComparison.Ordinal);
        Assert.True(
            exception.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("union", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("arm", StringComparison.OrdinalIgnoreCase),
            $"Expected inactive union-arm validation message, got: {exception.Message}");
    }
}
