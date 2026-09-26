using System.Globalization;
using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryBooleanValueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tagged_copies_keep_value_equality_without_mutating_factory_values(bool value)
    {
        var original = QueryValue.FromBoolean(value);
        var tagged = original with { ParameterKey = "p7" };

        Assert.NotSame(original, tagged);
        Assert.Equal(original, tagged);
        Assert.Equal(original.GetHashCode(), tagged.GetHashCode());
        Assert.Equal("p7", tagged.ParameterKey);
        Assert.Null(original.ParameterKey);
        Assert.Null(QueryValue.FromBoolean(value).ParameterKey);
        Assert.Equal(value, QueryValue.FromBoolean(value).Boolean);
        Assert.Equal(value ? "true" : "false", tagged.ToCanonicalText());
        Assert.NotEqual(original, QueryValue.FromBoolean(!value));
        Assert.NotEqual(original, QueryValue.Null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Boolean_json_round_trips_without_capture_provenance(bool value)
    {
        var tagged = QueryValue.FromBoolean(value) with { ParameterKey = "p7" };

        var json = JsonSerializer.Serialize(tagged, EventQueryJson.Options);
        var restored = JsonSerializer.Deserialize<QueryValue>(json, EventQueryJson.Options);

        Assert.Equal(value ? "true" : "false", json);
        Assert.Equal(tagged, restored);
        Assert.Null(Assert.IsType<QueryValue>(restored).ParameterKey);
        Assert.Null(QueryValue.FromBoolean(value).ParameterKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Independent_captures_keep_separate_parameter_keys(bool value)
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<Sample>(e => e.First == value && e.Second == value);
        var first = Assert.IsType<QueryValue>(filter.Children[0].Value);
        var second = Assert.IsType<QueryValue>(filter.Children[1].Value);

        Assert.Equal("p0", first.ParameterKey);
        Assert.Equal("p1", second.ParameterKey);
        Assert.NotSame(first, second);
        Assert.Equal(value, first.Boolean);
        Assert.Equal(first, second);
        Assert.Null(QueryValue.FromBoolean(value).ParameterKey);
        Assert.Equal("p0", ExpressionQueryTranslator.TranslateFilter<Sample>(e => e.First == value).Value!.ParameterKey);
        Assert.Null(ExpressionQueryTranslator.TranslateFilter<Sample>(e => e.First).Value!.ParameterKey);
    }

    [Fact]
    public void Parallel_tagged_copies_do_not_share_capture_provenance()
    {
        var values = new QueryValue[32];
        Parallel.For(0, values.Length, index =>
            values[index] = QueryValue.FromBoolean(index % 2 == 0) with { ParameterKey = index.ToString(CultureInfo.InvariantCulture) });

        for (var index = 0; index < values.Length; index++)
        {
            Assert.Equal(index % 2 == 0, values[index].Boolean);
            Assert.Equal(index.ToString(CultureInfo.InvariantCulture), values[index].ParameterKey);
        }

        Assert.Null(QueryValue.FromBoolean(false).ParameterKey);
        Assert.Null(QueryValue.FromBoolean(true).ParameterKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void Nullable_boolean_normalization_preserves_the_null_kind(bool? value)
    {
        Assert.True(QueryValue.TryFromObject(value, out var actual));
        Assert.Equal(value.HasValue ? QueryValueKind.Boolean : QueryValueKind.Null, actual.Kind);
        Assert.Equal(value.GetValueOrDefault(), actual.Boolean);
        Assert.Null(actual.ParameterKey);
    }

    private sealed record Sample(bool First, bool Second);
}
