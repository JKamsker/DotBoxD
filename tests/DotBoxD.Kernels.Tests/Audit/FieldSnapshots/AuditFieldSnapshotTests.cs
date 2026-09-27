namespace DotBoxD.Kernels.Tests.Audit;

public sealed class AuditFieldSnapshotTests
{
    [Theory]
    [InlineData("dictionary", false)]
    [InlineData("dictionary", true)]
    [InlineData("read-only", false)]
    [InlineData("read-only", true)]
    [InlineData("enumerable", false)]
    [InlineData("enumerable", true)]
    [InlineData("case-insensitive", false)]
    [InlineData("case-insensitive", true)]
    public void Fields_are_captured_once_in_an_independent_ordinal_snapshot(string kind, bool recordCopy)
    {
        var fields = AuditFieldSnapshotFixture.Fields();
        var input = AuditFieldSnapshotFixture.Input(kind, fields);
        var template = AuditFieldSnapshotFixture.Event(null);
        var auditEvent = recordCopy ? template with { Fields = input } : AuditFieldSnapshotFixture.Event(input);
        Assert.Null(template.Fields);
        Assert.NotSame(input, auditEvent.Fields);
        if (input is AuditFieldSnapshotFixture.ObservedDictionary observed)
        {
            Assert.Equal(1, observed.Enumerations);
        }

        var source = input as Dictionary<string, string> ?? fields;
        source["Alpha"] = "changed";
        source.Clear();
        var snapshot = Assert.IsAssignableFrom<IDictionary<string, string>>(auditEvent.Fields);
        Assert.Equal(2, snapshot.Count);
        Assert.Equal("one", snapshot["Alpha"]);
        Assert.Equal("two", snapshot["Beta"]);
        Assert.False(snapshot.ContainsKey("alpha"));
        Assert.True(snapshot.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => snapshot["Alpha"] = "changed");
    }

    [Theory]
    [InlineData("dictionary", "Alpha")]
    [InlineData("dictionary", "Beta")]
    [InlineData("read-only", "Alpha")]
    [InlineData("read-only", "Beta")]
    [InlineData("enumerable", "Alpha")]
    [InlineData("enumerable", "Beta")]
    [InlineData("case-insensitive", "Alpha")]
    [InlineData("case-insensitive", "Beta")]
    public void Null_field_values_are_rejected_with_the_public_parameter_name(string kind, string key)
    {
        var fields = AuditFieldSnapshotFixture.Fields();
        fields[key] = null!;
        var input = AuditFieldSnapshotFixture.Input(kind, fields);
        var exception = Assert.Throws<ArgumentException>(() => AuditFieldSnapshotFixture.Event(input));
        Assert.Equal("Fields", exception.ParamName);
    }

    [Theory]
    [InlineData("dictionary")]
    [InlineData("read-only")]
    [InlineData("enumerable")]
    [InlineData("case-insensitive")]
    public void Empty_field_collections_remain_non_null_and_read_only(string kind)
    {
        var input = AuditFieldSnapshotFixture.Input(kind, new Dictionary<string, string>(StringComparer.Ordinal));
        var auditEvent = AuditFieldSnapshotFixture.Event(input);
        var snapshot = Assert.IsAssignableFrom<IDictionary<string, string>>(auditEvent.Fields);
        Assert.Empty(snapshot);
        Assert.Throws<NotSupportedException>(() => snapshot.Add("field", "value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Null_fields_remain_optional(bool recordCopy)
    {
        var template = AuditFieldSnapshotFixture.Event(AuditFieldSnapshotFixture.Fields());
        var auditEvent = recordCopy ? template with { Fields = null } : AuditFieldSnapshotFixture.Event(null);
        Assert.Null(auditEvent.Fields);
        Assert.Equal(2, template.Fields!.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_failures_propagate_without_changing_existing_events(bool recordCopy)
    {
        var failure = new InvalidOperationException("field enumeration failed");
        var input = new AuditFieldSnapshotFixture.ObservedDictionary(AuditFieldSnapshotFixture.Fields(), failure);
        var template = AuditFieldSnapshotFixture.Event(null);
        var actual = Assert.Throws<InvalidOperationException>(() =>
            recordCopy ? template with { Fields = input } : AuditFieldSnapshotFixture.Event(input));
        Assert.Same(failure, actual);
        Assert.Null(template.Fields);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Case_distinct_empty_and_unicode_field_names_are_preserved(bool recordCopy)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Alpha"] = "upper",
            ["alpha"] = "lower",
            [""] = "empty",
            ["β"] = "unicode",
        };
        var template = AuditFieldSnapshotFixture.Event(null);
        var auditEvent = recordCopy ? template with { Fields = fields } : AuditFieldSnapshotFixture.Event(fields);
        Assert.Equal(4, auditEvent.Fields!.Count);
        Assert.Equal("upper", auditEvent.Fields["Alpha"]);
        Assert.Equal("lower", auditEvent.Fields["alpha"]);
        Assert.Equal("empty", auditEvent.Fields[""]);
        Assert.Equal("unicode", auditEvent.Fields["β"]);
    }
}
