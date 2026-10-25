using System.Collections.Immutable;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiMaterializationValidatorDuplicatePropertyTests
{
    [Fact]
    public void Duplicate_property_entries_fail_closed()
    {
        var policy = new UiPolicy();

        var duplicateMaximum = ImmutableArray.Create(
            new UiPropertyValue(1, UiPropertyId.Maximum, UiValue.FromNumber(10)),
            new UiPropertyValue(1, UiPropertyId.Maximum, UiValue.FromNumber(20)));
        var duplicateValue = ImmutableArray.Create(
            new UiPropertyValue(1, UiPropertyId.Value, UiValue.FromNumber(10)),
            new UiPropertyValue(1, UiPropertyId.Value, UiValue.FromNumber(20)));

        Assert.Throws<UiValidationException>(() =>
            UiMaterializationValidator.Validate(0, duplicateMaximum, policy));
        Assert.Throws<UiValidationException>(() =>
            UiMaterializationValidator.Validate(0, duplicateValue, policy));
    }

    [Fact]
    public void Unique_maximum_and_value_entries_for_a_node_validate()
    {
        var values = ImmutableArray.Create(
            new UiPropertyValue(1, UiPropertyId.Maximum, UiValue.FromNumber(20)),
            new UiPropertyValue(1, UiPropertyId.Value, UiValue.FromNumber(10)));

        UiMaterializationValidator.Validate(0, values, new UiPolicy());
    }
}
