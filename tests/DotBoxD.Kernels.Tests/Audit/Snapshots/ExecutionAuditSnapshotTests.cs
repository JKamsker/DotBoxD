using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class ExecutionAuditSnapshotTests
{
    [Theory]
    [InlineData("array", false)]
    [InlineData("array", true)]
    [InlineData("list", false)]
    [InlineData("list", true)]
    [InlineData("read-only-array", false)]
    [InlineData("read-only-array", true)]
    [InlineData("enumerable-list", false)]
    [InlineData("enumerable-list", true)]
    public void External_events_are_captured_once_in_an_immutable_ordered_snapshot(string kind, bool recordCopy)
    {
        var events = AuditSnapshotFixture.Events();
        var first = events[0];
        var second = events[1];
        var input = AuditSnapshotFixture.Input(kind, events);
        var template = AuditSnapshotFixture.Result([]);
        var result = recordCopy ? template with { AuditEvents = input } : AuditSnapshotFixture.Result(input);
        Assert.Empty(template.AuditEvents);
        Assert.NotSame(input, result.AuditEvents);
        if (input is AuditSnapshotFixture.CountingList counted)
        {
            Assert.Equal(1, counted.Enumerations);
        }

        var replacement = first with { Kind = "replacement" };
        if (input is List<SandboxAuditEvent> list)
        {
            list[0] = replacement;
            list.Clear();
        }
        else
        {
            events[0] = replacement;
        }
        Assert.Equal(2, result.AuditEvents.Count);
        Assert.Same(first, result.AuditEvents[0]);
        Assert.Same(second, result.AuditEvents[1]);
        var snapshot = Assert.IsAssignableFrom<IList<SandboxAuditEvent>>(result.AuditEvents);
        Assert.True(snapshot.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => snapshot[0] = replacement);
    }

    [Theory]
    [InlineData("array", 0)]
    [InlineData("array", 1)]
    [InlineData("list", 0)]
    [InlineData("list", 1)]
    [InlineData("read-only-array", 0)]
    [InlineData("read-only-array", 1)]
    [InlineData("enumerable-list", 0)]
    [InlineData("enumerable-list", 1)]
    public void External_null_entries_are_rejected_with_the_public_parameter_name(string kind, int position)
    {
        var events = AuditSnapshotFixture.Events();
        events[position] = null!;
        var input = AuditSnapshotFixture.Input(kind, events);
        var exception = Assert.Throws<ArgumentException>(() => AuditSnapshotFixture.Result(input));
        Assert.Equal("AuditEvents", exception.ParamName);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("list")]
    [InlineData("read-only-array")]
    [InlineData("enumerable-list")]
    public void Empty_external_collections_produce_an_immutable_empty_snapshot(string kind)
    {
        var input = AuditSnapshotFixture.Input(kind, []);
        var result = AuditSnapshotFixture.Result(input);
        Assert.Empty(result.AuditEvents);
        var snapshot = Assert.IsAssignableFrom<IList<SandboxAuditEvent>>(result.AuditEvents);
        Assert.Throws<NotSupportedException>(() => snapshot.Add(AuditSnapshotFixture.Events()[0]));
    }

    [Theory]
    [InlineData("owned-array", false)]
    [InlineData("owned-array", true)]
    [InlineData("owned-list", false)]
    [InlineData("owned-list", true)]
    public void Trusted_owned_snapshots_are_adopted_without_another_copy(string kind, bool recordCopy)
    {
        var input = AuditSnapshotFixture.Input(kind, AuditSnapshotFixture.Events());
        var template = AuditSnapshotFixture.Result([]);
        var result = recordCopy ? template with { AuditEvents = input } : AuditSnapshotFixture.Result(input);
        Assert.Same(input, result.AuditEvents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enumeration_errors_propagate_without_changing_existing_results(bool recordCopy)
    {
        var failure = new InvalidOperationException("source enumeration failed");
        var input = new AuditSnapshotFixture.ThrowingList(AuditSnapshotFixture.Events(), failure);
        var template = AuditSnapshotFixture.Result([]);
        var actual = Assert.Throws<InvalidOperationException>(() =>
            recordCopy ? template with { AuditEvents = input } : AuditSnapshotFixture.Result(input));
        Assert.Same(failure, actual);
        Assert.Empty(template.AuditEvents);
    }
}
