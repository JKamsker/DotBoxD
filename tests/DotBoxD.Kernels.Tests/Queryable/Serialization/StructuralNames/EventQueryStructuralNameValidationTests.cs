using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryStructuralNameValidationTests
{
    [Theory]
    [InlineData(StructuralNameTarget.EventName)]
    [InlineData(StructuralNameTarget.ConstructTypeName)]
    [InlineData(StructuralNameTarget.ProjectionFieldName)]
    public void Public_factories_reject_whitespace_structural_names(StructuralNameTarget target)
        => AssertStructuralNameRejection(PublicFactoryAction(target), target);

    [Theory]
    [InlineData(StructuralNameTarget.EventName)]
    [InlineData(StructuralNameTarget.ConstructTypeName)]
    [InlineData(StructuralNameTarget.ProjectionFieldName)]
    public void Serialization_rejects_whitespace_structural_names(StructuralNameTarget target)
        => AssertStructuralNameRejection(() => EventQueryJson.Serialize(DocumentWithWhitespaceName(target)), target);

    [Theory]
    [InlineData(StructuralNameTarget.EventName)]
    [InlineData(StructuralNameTarget.ConstructTypeName)]
    [InlineData(StructuralNameTarget.ProjectionFieldName)]
    public void Fingerprinting_rejects_whitespace_structural_names(StructuralNameTarget target)
        => AssertStructuralNameRejection(() => QueryFingerprint.Compute(DocumentWithWhitespaceName(target)), target);

    [Theory]
    [InlineData(StructuralNameTarget.EventName)]
    [InlineData(StructuralNameTarget.ConstructTypeName)]
    [InlineData(StructuralNameTarget.ProjectionFieldName)]
    public void Deserialization_rejects_whitespace_structural_names(StructuralNameTarget target)
        => AssertStructuralNameRejection(() => EventQueryJson.Deserialize(JsonWithWhitespaceName(target)), target);

    private static Action PublicFactoryAction(StructuralNameTarget target)
        => target switch
        {
            StructuralNameTarget.EventName => () => EventQueryDocument.Create(
                "   ",
                QueryFilter.MatchAll,
                QueryProjection.Identity),
            StructuralNameTarget.ConstructTypeName => () => QueryProjection.Construct(
                "   ",
                [QueryProjectionField.FromMember("damage", "Damage")]),
            StructuralNameTarget.ProjectionFieldName => () => QueryProjectionField.FromMember("   ", "Damage"),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    private static EventQueryDocument DocumentWithWhitespaceName(StructuralNameTarget target)
        => target switch
        {
            StructuralNameTarget.EventName => new EventQueryDocument
            {
                EventName = "   ",
                Filter = QueryFilter.MatchAll,
                Projection = QueryProjection.Identity,
            },
            StructuralNameTarget.ConstructTypeName => EventQueryDocument.Create(
                "AttackEvent",
                QueryFilter.MatchAll,
                new QueryProjection
                {
                    Kind = QueryProjectionKind.Construct,
                    TypeName = "   ",
                    Fields = [QueryProjectionField.FromMember("damage", "Damage")],
                }),
            StructuralNameTarget.ProjectionFieldName => EventQueryDocument.Create(
                "AttackEvent",
                QueryFilter.MatchAll,
                new QueryProjection
                {
                    Kind = QueryProjectionKind.Construct,
                    TypeName = "DamageNotice",
                    Fields =
                    [
                        new QueryProjectionField
                        {
                            Name = "   ",
                            Path = "Damage",
                        },
                    ],
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    private static string JsonWithWhitespaceName(StructuralNameTarget target)
        => target switch
        {
            StructuralNameTarget.EventName => """{"event":"   ","projection":{"kind":"identity"}}""",
            StructuralNameTarget.ConstructTypeName => """
                {"event":"AttackEvent","projection":{"kind":"construct","type":"   ","fields":[{"name":"damage","path":"Damage"}]}}
                """,
            StructuralNameTarget.ProjectionFieldName => """
                {"event":"AttackEvent","projection":{"kind":"construct","type":"DamageNotice","fields":[{"name":"   ","path":"Damage"}]}}
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    private static void AssertStructuralNameRejection(Action action, StructuralNameTarget target)
    {
        var exception = Record.Exception(action);

        Assert.NotNull(exception);
        Assert.True(
            exception is ArgumentException or InvalidOperationException or JsonException,
            $"Expected structural-name validation, got {exception.GetType().Name}: {exception.Message}");
        AssertStructuralNameMessage(exception, target);
    }

    private static void AssertStructuralNameMessage(Exception exception, StructuralNameTarget target)
    {
        Assert.True(
            exception.Message.Contains(ExpectedMessageToken(target), StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("structural", StringComparison.OrdinalIgnoreCase),
            $"Expected message to name {target}, got: {exception.Message}");
        Assert.True(
            exception.Message.Contains("white", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("blank", StringComparison.OrdinalIgnoreCase),
            $"Expected message to explain whitespace/empty rejection, got: {exception.Message}");
    }

    private static string ExpectedMessageToken(StructuralNameTarget target)
        => target switch
        {
            StructuralNameTarget.EventName => "event",
            StructuralNameTarget.ConstructTypeName => "type",
            StructuralNameTarget.ProjectionFieldName => "name",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    public enum StructuralNameTarget
    {
        EventName,
        ConstructTypeName,
        ProjectionFieldName,
    }
}
