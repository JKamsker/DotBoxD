using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryAstInitializerSnapshotTests
{
    [Fact]
    public void Projection_fields_initializer_snapshots_caller_owned_list()
    {
        var fields = new List<QueryProjectionField>
        {
            QueryProjectionField.FromMember("attacker", "AttackerId"),
        };
        var document = EventQueryDocument.Create(
            "AttackEvent",
            QueryFilter.MatchAll,
            new QueryProjection
            {
                Kind = QueryProjectionKind.Construct,
                TypeName = "Notice",
                Fields = fields,
            });

        AssertDocumentIsStableAfterMutation(
            document,
            () => fields[0] = QueryProjectionField.FromMember("target", "TargetId"));
    }

    [Fact]
    public void Filter_values_initializer_snapshots_caller_owned_list()
    {
        var values = new List<QueryValue>
        {
            QueryValue.FromString("allowed"),
        };
        var document = EventQueryDocument.Create(
            "AttackEvent",
            new QueryFilter
            {
                Kind = QueryFilterKind.In,
                Field = "AttackerId",
                Values = values,
            },
            QueryProjection.Identity);

        AssertDocumentIsStableAfterMutation(document, () => values[0] = QueryValue.FromString("blocked"));
    }

    [Fact]
    public void Filter_children_initializer_snapshots_caller_owned_list()
    {
        var children = new List<QueryFilter>
        {
            QueryFilter.Compare("AttackerId", QueryComparisonOperator.Equal, QueryValue.FromString("allowed")),
        };
        var document = EventQueryDocument.Create(
            "AttackEvent",
            new QueryFilter
            {
                Kind = QueryFilterKind.And,
                Children = children,
            },
            QueryProjection.Identity);

        AssertDocumentIsStableAfterMutation(
            document,
            () => children[0] = QueryFilter.Compare(
                "AttackerId",
                QueryComparisonOperator.Equal,
                QueryValue.FromString("blocked")));
    }

    private static void AssertDocumentIsStableAfterMutation(EventQueryDocument document, Action mutate)
    {
        var json = EventQueryJson.Serialize(document);
        var fingerprint = QueryFingerprint.Compute(document);

        mutate();

        Assert.Equal(json, EventQueryJson.Serialize(document));
        Assert.Equal(fingerprint, QueryFingerprint.Compute(document));
    }
}
