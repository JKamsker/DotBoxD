using System.Collections.Immutable;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI.Validation;

public sealed class UiMaterializationValidatorValidationTests
{
    [Fact]
    public void Static_node_count_must_be_nonnegative_before_counting_dynamic_rows()
    {
        var policy = new UiPolicy { MaxNodes = 2, MaxChildren = 5 };
        var fiveRows = ItemValues(5);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => UiMaterializationValidator.Validate(-3, fiveRows, policy));

        Assert.Equal("staticNodeCount", exception.ParamName);
        Assert.Throws<UiValidationException>(() => UiMaterializationValidator.Validate(0, fiveRows, policy));
        UiMaterializationValidator.Validate(1, ItemValues(1), policy);
    }

    private static ImmutableArray<UiPropertyValue> ItemValues(int count)
    {
        var rows = Enumerable.Range(0, count)
            .Select(index => new UiListItem($"key-{index}", $"Row {index}"))
            .ToImmutableArray();
        return [new UiPropertyValue(1, UiPropertyId.Items, UiValue.FromItems(rows))];
    }

    [Fact]
    public void Null_property_entries_fail_closed_at_the_materialization_boundary()
    {
        Assert.Throws<UiValidationException>(() =>
            UiMaterializationValidator.Validate(0, ImmutableArray.CreateRange<UiPropertyValue>([null!]), new UiPolicy()));
    }
}
