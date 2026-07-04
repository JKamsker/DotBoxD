using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryProjectionPathValidationTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("Attacker Id")]
    [InlineData(".Damage")]
    [InlineData("Damage.")]
    [InlineData("Damage..Amount")]
    [InlineData("9Damage")]
    public void Member_projection_factory_rejects_invalid_member_paths(string path)
        => AssertProjectionPathRejection(() => QueryProjection.Member(path));

    [Theory]
    [InlineData(" ")]
    [InlineData("Attacker Id")]
    [InlineData(".Damage")]
    [InlineData("Damage.")]
    [InlineData("Damage..Amount")]
    [InlineData("9Damage")]
    public void Construct_field_factory_rejects_invalid_member_paths(string path)
        => AssertProjectionPathRejection(() => QueryProjectionField.FromMember("damage", path));

    [Theory]
    [InlineData(" ")]
    [InlineData("Attacker Id")]
    [InlineData(".Damage")]
    [InlineData("Damage.")]
    [InlineData("Damage..Amount")]
    [InlineData("9Damage")]
    public void Document_serialization_rejects_invalid_projection_member_paths(string path)
    {
        var document = EventQueryDocument.Create(
            "AttackEvent",
            QueryFilter.MatchAll,
            new QueryProjection { Kind = QueryProjectionKind.Member, Path = path });

        AssertProjectionPathRejection(() => EventQueryJson.Serialize(document));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Attacker Id")]
    [InlineData(".Damage")]
    [InlineData("Damage.")]
    [InlineData("Damage..Amount")]
    [InlineData("9Damage")]
    public void Document_serialization_rejects_invalid_construct_field_paths(string path)
    {
        var document = EventQueryDocument.Create(
            "AttackEvent",
            QueryFilter.MatchAll,
            new QueryProjection
            {
                Kind = QueryProjectionKind.Construct,
                TypeName = "AttackNotice",
                Fields = [new QueryProjectionField { Name = "damage", Path = path }],
            });

        AssertProjectionPathRejection(() => EventQueryJson.Serialize(document));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Attacker Id")]
    [InlineData(".Damage")]
    [InlineData("Damage.")]
    [InlineData("Damage..Amount")]
    [InlineData("9Damage")]
    public void Projection_json_rejects_invalid_member_paths(string path)
    {
        var json = "{\"event\":\"AttackEvent\",\"projection\":{\"kind\":\"member\",\"path\":"
            + JsonSerializer.Serialize(path)
            + "}}";

        AssertProjectionPathRejection(() => EventQueryJson.Deserialize(json));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Attacker Id")]
    [InlineData(".Damage")]
    [InlineData("Damage.")]
    [InlineData("Damage..Amount")]
    [InlineData("9Damage")]
    public void Projection_json_rejects_invalid_construct_field_paths(string path)
    {
        var json = "{\"event\":\"AttackEvent\",\"projection\":{\"kind\":\"construct\",\"type\":\"AttackNotice\","
            + "\"fields\":[{\"name\":\"damage\",\"path\":"
            + JsonSerializer.Serialize(path)
            + "}]}}";

        AssertProjectionPathRejection(() => EventQueryJson.Deserialize(json));
    }

    private static void AssertProjectionPathRejection(Action action)
    {
        var exception = Record.Exception(action);

        Assert.NotNull(exception);
        Assert.True(
            exception is ArgumentException or InvalidOperationException or JsonException,
            $"Expected QueryProjection path validation, got {exception.GetType().Name}: {exception.Message}");
        Assert.Contains("QueryProjection", exception.Message, StringComparison.Ordinal);
        Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            exception.Message.Contains("valid", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("identifier", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("segment", StringComparison.OrdinalIgnoreCase),
            $"Expected projection path validation message, got: {exception.Message}");
    }
}
