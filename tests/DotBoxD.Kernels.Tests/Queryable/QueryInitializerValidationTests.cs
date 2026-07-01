using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Text;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryInitializerValidationTests
{
    private static readonly MemberValueReader Reader = new();
    private static readonly NullableTestEvent SampleEvent = new("key", 1);

    [Fact]
    public void Public_compare_filter_initializer_without_value_is_rejected()
    {
        var filter = new QueryFilter
        {
            Kind = QueryFilterKind.Compare,
            Field = "Key",
            Operator = QueryComparisonOperator.Equal,
        };

        var evaluation = Assert.Throws<InvalidOperationException>(() =>
            QueryFilterEvaluator.Evaluate(filter, new NullableTestEvent(null, 1), new MemberValueReader()));
        Assert.Contains("Compare", evaluation.Message, StringComparison.Ordinal);
        Assert.Contains("Value", evaluation.Message, StringComparison.Ordinal);

        var formatting = Assert.Throws<InvalidOperationException>(() => QueryText.Format(filter));
        Assert.Contains("Compare", formatting.Message, StringComparison.Ordinal);
        Assert.Contains("Value", formatting.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => QueryFilterCompiler.Compile(filter, new MemberValueReader()));
        Assert.Throws<InvalidOperationException>(() => EventQueryPlanner.Plan(filter));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(filter, EventQueryJson.Options));
    }

    [Fact]
    public void Public_compare_filter_initializer_with_empty_field_is_rejected_by_formatter()
        => AssertQueryFieldRejected(QueryText.Format, CompareWithEmptyField(), "Compare");

    [Fact]
    public void Public_compare_filter_initializer_with_empty_field_is_rejected_by_json_writer()
        => AssertQueryFieldRejected(
            filter => JsonSerializer.Serialize(filter, EventQueryJson.Options),
            CompareWithEmptyField(),
            "Compare");

    [Fact]
    public void Public_compare_filter_initializer_with_empty_field_is_rejected_by_planner()
        => AssertQueryFieldRejected(EventQueryPlanner.Plan, CompareWithEmptyField(), "Compare");

    [Fact]
    public void Public_compare_filter_initializer_with_empty_field_is_rejected_by_evaluator()
        => AssertQueryFieldRejected(
            filter => QueryFilterEvaluator.Evaluate(filter, SampleEvent, Reader),
            CompareWithEmptyField(),
            "Compare");

    [Fact]
    public void Public_compare_filter_initializer_with_empty_field_is_rejected_by_compiler()
        => AssertQueryFieldRejected(
            filter => QueryFilterCompiler.Compile(filter, Reader),
            CompareWithEmptyField(),
            "Compare");

    [Fact]
    public void Public_in_filter_initializer_with_empty_field_is_rejected_by_formatter()
        => AssertQueryFieldRejected(QueryText.Format, InWithEmptyField(), "In");

    [Fact]
    public void Public_in_filter_initializer_with_empty_field_is_rejected_by_json_writer()
        => AssertQueryFieldRejected(
            filter => JsonSerializer.Serialize(filter, EventQueryJson.Options),
            InWithEmptyField(),
            "In");

    [Fact]
    public void Public_in_filter_initializer_with_empty_field_is_rejected_by_planner()
        => AssertQueryFieldRejected(EventQueryPlanner.Plan, InWithEmptyField(), "In");

    [Fact]
    public void Public_in_filter_initializer_with_empty_field_is_rejected_by_evaluator()
        => AssertQueryFieldRejected(
            filter => QueryFilterEvaluator.Evaluate(filter, SampleEvent, Reader),
            InWithEmptyField(),
            "In");

    [Fact]
    public void Public_in_filter_initializer_with_empty_field_is_rejected_by_compiler()
        => AssertQueryFieldRejected(
            filter => QueryFilterCompiler.Compile(filter, Reader),
            InWithEmptyField(),
            "In");

    [Fact]
    public void Public_member_projection_initializer_without_path_is_rejected_on_write()
    {
        var projection = new QueryProjection { Kind = QueryProjectionKind.Member };

        var exception = Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Serialize(projection, EventQueryJson.Options));
        Assert.True(exception is JsonException or InvalidOperationException);
        Assert.Contains("Member", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static QueryFilter CompareWithEmptyField()
        => new()
        {
            Kind = QueryFilterKind.Compare,
            Operator = QueryComparisonOperator.Equal,
            Value = QueryValue.FromInteger(5),
        };

    private static QueryFilter InWithEmptyField()
        => new()
        {
            Kind = QueryFilterKind.In,
            Values = [QueryValue.FromInteger(5)],
        };

    private static void AssertQueryFieldRejected<T>(
        Func<QueryFilter, T> action,
        QueryFilter filter,
        string leafKind)
    {
        var exception = Assert.ThrowsAny<Exception>(() => action(filter));
        Assert.Contains("QueryFilter", exception.Message, StringComparison.Ordinal);
        Assert.Contains(leafKind, exception.Message, StringComparison.Ordinal);
        Assert.True(
            exception.Message.Contains("field", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("path", StringComparison.OrdinalIgnoreCase),
            $"Expected a field/path validation message, but got: {exception.Message}");
    }
}
