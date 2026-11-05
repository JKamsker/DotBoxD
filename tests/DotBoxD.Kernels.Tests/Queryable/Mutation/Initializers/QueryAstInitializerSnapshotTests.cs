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

    [Fact]
    public void Document_snapshots_expose_read_only_collections()
    {
        var document = EventQueryDocument.Create(
            "AttackEvent",
            new QueryFilter
            {
                Kind = QueryFilterKind.And,
                Children =
                [
                    new QueryFilter
                    {
                        Kind = QueryFilterKind.In,
                        Field = "AttackerId",
                        Values = [QueryValue.FromString("allowed")],
                    },
                ],
            },
            new QueryProjection
            {
                Kind = QueryProjectionKind.Construct,
                TypeName = "Notice",
                Fields = [QueryProjectionField.FromMember("attacker", "AttackerId")],
            });

        AssertReadOnly(document.Filter.Children[0].Values, QueryValue.FromString("blocked"));
        AssertReadOnly(document.Filter.Children, QueryFilter.MatchAll);
        AssertReadOnly(document.Projection.Fields, QueryProjectionField.FromMember("target", "TargetId"));
    }

    [Fact]
    public void Equivalent_document_snapshots_have_value_equality()
    {
        var left = CreateDocument();
        var right = CreateDocument();

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Contains(right, new HashSet<EventQueryDocument> { left });
    }

    private static void AssertDocumentIsStableAfterMutation(EventQueryDocument document, Action mutate)
    {
        var json = EventQueryJson.Serialize(document);
        var fingerprint = QueryFingerprint.Compute(document);

        mutate();

        Assert.Equal(json, EventQueryJson.Serialize(document));
        Assert.Equal(fingerprint, QueryFingerprint.Compute(document));
    }

    private static EventQueryDocument CreateDocument() => EventQueryDocument.Create(
        "AttackEvent",
        new QueryFilter
        {
            Kind = QueryFilterKind.And,
            Children =
            [
                QueryFilter.Compare("AttackerId", QueryComparisonOperator.Equal, QueryValue.FromString("allowed")),
                new QueryFilter
                {
                    Kind = QueryFilterKind.In,
                    Field = "TargetId",
                    Values = [QueryValue.FromString("target")],
                },
            ],
        },
        new QueryProjection
        {
            Kind = QueryProjectionKind.Construct,
            TypeName = "Notice",
            Fields = [QueryProjectionField.FromMember("attacker", "AttackerId")],
        });

    private static void AssertReadOnly<T>(IReadOnlyList<T> values, T replacement)
    {
        var writable = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.Throws<NotSupportedException>(() => writable[0] = replacement);
    }
}
