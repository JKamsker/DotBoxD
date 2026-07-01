using System.Text.Json;
using DotBoxD.Queryable.Analysis;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Text;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryInitializerValidationTests
{
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
    public void Public_member_projection_initializer_without_path_is_rejected_on_write()
    {
        var projection = new QueryProjection { Kind = QueryProjectionKind.Member };

        var exception = Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Serialize(projection, EventQueryJson.Options));
        Assert.True(exception is JsonException or InvalidOperationException);
        Assert.Contains("Member", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Public_not_filter_initializer_without_child_is_rejected_by_satisfiability()
    {
        var filter = NotWithoutChild();

        AssertNotChildRejection(() => QuerySatisfiability.IsSatisfiable(filter));
        AssertNotChildRejection(() => QuerySatisfiability.EnsureSatisfiable(filter));
    }

    [Fact]
    public void Public_not_filter_initializer_without_child_is_rejected_by_text_formatting()
    {
        var filter = NotWithoutChild();

        AssertNotChildRejection(() => QueryText.Format(filter));
    }

    [Fact]
    public void Public_not_filter_initializer_without_child_is_rejected_by_json_serialization()
    {
        var filter = NotWithoutChild();

        var exception = Record.Exception(() => JsonSerializer.Serialize(filter, EventQueryJson.Options));
        Assert.NotNull(exception);
        Assert.True(exception is InvalidOperationException or JsonException);
        AssertNotChildMessage(exception);
    }

    [Fact]
    public void Public_not_filter_initializer_without_child_is_rejected_by_evaluator()
    {
        var filter = NotWithoutChild();

        AssertNotChildRejection(() =>
            QueryFilterEvaluator.Evaluate(filter, new NullableTestEvent(null, 1), new MemberValueReader()));
    }

    [Fact]
    public void Public_not_filter_initializer_without_child_is_rejected_by_compiler()
    {
        var filter = NotWithoutChild();

        AssertNotChildRejection(() => QueryFilterCompiler.Compile(filter, new MemberValueReader()));
    }

    [Fact]
    public void Public_not_filter_initializer_without_child_is_rejected_by_planner()
    {
        var filter = NotWithoutChild();

        AssertNotChildRejection(() => EventQueryPlanner.Plan(filter));
    }

    private static QueryFilter NotWithoutChild() => new() { Kind = QueryFilterKind.Not };

    private static void AssertNotChildRejection(Action action)
    {
        var exception = Assert.Throws<InvalidOperationException>(action);
        AssertNotChildMessage(exception);
    }

    private static void AssertNotChildMessage(Exception exception)
    {
        Assert.Contains("Not", exception.Message, StringComparison.Ordinal);
        Assert.Contains("child", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
