using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable.Serialization.PathValidation;

public sealed class QueryProjectionPathValidationTests
{
    public static TheoryData<string> InvalidMemberPaths => new()
    {
        " ",
        "Attacker Id",
        ".Damage",
        "Damage.",
        "Damage..Amount",
        "9Damage",
    };

    [Theory]
    [MemberData(nameof(InvalidMemberPaths))]
    public void Member_projection_factory_rejects_invalid_member_paths(string path)
        => AssertProjectionPathRejection(() => QueryProjection.Member(path));

    [Theory]
    [MemberData(nameof(InvalidMemberPaths))]
    public void Construct_field_factory_rejects_invalid_member_paths(string path)
        => AssertProjectionPathRejection(() => QueryProjectionField.FromMember("damage", path));

    [Theory]
    [MemberData(nameof(InvalidMemberPaths))]
    public void Document_serialization_rejects_invalid_projection_member_paths(string path)
    {
        var document = new EventQueryDocument
        {
            EventName = "AttackEvent",
            Filter = QueryFilter.MatchAll,
            Projection = new QueryProjection { Kind = QueryProjectionKind.Member, Path = path },
        };

        AssertProjectionPathRejection(() => EventQueryJson.Serialize(document));
    }

    [Theory]
    [MemberData(nameof(InvalidMemberPaths))]
    public void Document_serialization_rejects_invalid_construct_field_paths(string path)
    {
        var document = new EventQueryDocument
        {
            EventName = "AttackEvent",
            Filter = QueryFilter.MatchAll,
            Projection = new QueryProjection
            {
                Kind = QueryProjectionKind.Construct,
                TypeName = "AttackNotice",
                Fields = [new QueryProjectionField { Name = "damage", Path = path }],
            },
        };

        AssertProjectionPathRejection(() => EventQueryJson.Serialize(document));
    }

    [Theory]
    [MemberData(nameof(InvalidMemberPaths))]
    public void Projection_json_rejects_invalid_member_paths(string path)
    {
        var json = "{\"event\":\"AttackEvent\",\"projection\":{\"kind\":\"member\",\"path\":"
            + JsonSerializer.Serialize(path)
            + "}}";

        AssertProjectionPathRejection(() => EventQueryJson.Deserialize(json));
    }

    [Theory]
    [MemberData(nameof(InvalidMemberPaths))]
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

        if (exception is ArgumentException argumentException)
        {
            Assert.Equal("path", argumentException.ParamName);
        }
    }
}
