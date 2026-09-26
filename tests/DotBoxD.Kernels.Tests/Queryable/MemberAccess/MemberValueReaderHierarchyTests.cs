using System.Linq.Expressions;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class MemberValueReaderHierarchyTests
{
    [Theory]
    [InlineData("Field", false)]
    [InlineData("Field", true)]
    [InlineData("FieldLeaf", false)]
    [InlineData("FieldLeaf", true)]
    [InlineData("Property", false)]
    [InlineData("Property", true)]
    [InlineData("PropertyLeaf", false)]
    [InlineData("PropertyLeaf", true)]
    [InlineData("PropertyType", false)]
    [InlineData("PropertyType", true)]
    [InlineData("PropertyTypeLeaf", false)]
    [InlineData("PropertyTypeLeaf", true)]
    [InlineData("SameTypeProperty", false)]
    [InlineData("SameTypeProperty", true)]
    public void Readers_select_the_nearest_declared_public_member(string shape, bool declaredType)
    {
        object value = shape switch
        {
            "Field" => new FieldValue(),
            "FieldLeaf" => new FieldLeaf(),
            "Property" => new PropertyValue(),
            "PropertyLeaf" => new PropertyLeaf(),
            "PropertyType" => new StringValue(),
            "PropertyTypeLeaf" => new StringLeaf(),
            "SameTypeProperty" => new IntValue(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        object expected = shape is "PropertyType" or "PropertyTypeLeaf" ? "seven" : 7;
        var reader = declaredType ? new MemberValueReader(value.GetType()) : new MemberValueReader();

        Assert.Equal(expected, reader.Read(value, "Value"));
        Assert.Equal(expected, reader.Read(value, "Value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authored_queries_preserve_member_identity_within_the_declared_type_hierarchy(bool stringMember)
    {
        if (stringMember)
        {
            await AssertQuery(new StringValue(), e => e.Value == "seven", e => e.Value, "seven");
        }
        else
        {
            await AssertQuery(new FieldValue(), e => e.Value == 7, e => e.Value, 7);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unreadable_hiding_properties_do_not_fall_back_to_readable_base_members(bool writeOnly)
    {
        object value = writeOnly ? new WriteOnlyValue() : new PrivateGetterValue();
        var error = Assert.Throws<InvalidOperationException>(() => new MemberValueReader().Read(value, "Value"));

        Assert.Contains("public parameterless getter", error.Message, StringComparison.Ordinal);
    }

    private static async Task AssertQuery<TEvent, TValue>(
        TEvent value,
        Expression<Func<TEvent, bool>> filter,
        Expression<Func<TEvent, TValue>> project,
        TValue expected)
    {
        var host = new EventQueryHost();
        var values = new List<TValue>();
        using var handle = await host.Query<TEvent>().Where(filter).Select(project)
            .SubscribeAsync((item, _) =>
            {
                values.Add(item);
                return ValueTask.CompletedTask;
            });
        await host.PublishAsync(value, new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        Assert.Equal(expected, Assert.Single(values));
    }

    private class PropertyBase
    {
        public int Value => 99;
    }

    private class FieldValue : PropertyBase
    {
        public new readonly int Value = 7;
    }

    private sealed class FieldLeaf : FieldValue;

    private class FieldBase
    {
        public readonly int Value = 99;
    }

    private class PropertyValue : FieldBase
    {
        public new int Value => 7;
    }

    private sealed class PropertyLeaf : PropertyValue;

    private class StringValue : PropertyBase
    {
        public new string Value => "seven";
    }

    private sealed class StringLeaf : StringValue;

    private sealed class IntValue : PropertyBase
    {
        public new int Value => 7;
    }

    private sealed class PrivateGetterValue : PropertyBase
    {
        public new int Value { private get; set; }
    }

    private sealed class WriteOnlyValue : PropertyBase
    {
        public new int Value
        {
            set { }
        }
    }
}
