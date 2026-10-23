using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI.Validation.PropertyKinds;

public sealed class UiMaterializationValidatorPropertyKindTests
{
    public static IEnumerable<object[]> MismatchedValues()
    {
        yield return [UiPropertyId.Text, UiValue.FromBoolean(true)];
        yield return [UiPropertyId.Enabled, UiValue.FromString("enabled")];
        yield return [UiPropertyId.Items, UiValue.FromString("items")];
    }

    [Theory]
    [MemberData(nameof(MismatchedValues))]
    public void Mismatched_property_value_kinds_fail_closed(UiPropertyId propertyId, UiValue value)
    {
        Assert.Throws<UiValidationException>(() => UiMaterializationValidator.Validate(
            1, [new UiPropertyValue(1, propertyId, value)], new UiPolicy()));
    }

    [Fact]
    public void Matching_property_value_kinds_remain_valid()
    {
        UiMaterializationValidator.Validate(1,
        [
            new UiPropertyValue(1, UiPropertyId.Text, UiValue.FromString("text")),
            new UiPropertyValue(1, UiPropertyId.Enabled, UiValue.FromBoolean(true)),
            new UiPropertyValue(1, UiPropertyId.Items, UiValue.FromItems([]))
        ], new UiPolicy());
    }
}
