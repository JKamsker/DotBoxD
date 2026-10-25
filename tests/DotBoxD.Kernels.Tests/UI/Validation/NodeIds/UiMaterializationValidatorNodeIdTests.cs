using System.Collections.Immutable;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI.Validation.NodeIds;

public sealed class UiMaterializationValidatorNodeIdTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_property_node_ids_fail_closed(int nodeId)
    {
        var values = ImmutableArray.Create(
            new UiPropertyValue(nodeId, UiPropertyId.Text, UiValue.FromString("x")));

        var exception = Assert.Throws<UiValidationException>(() =>
            UiMaterializationValidator.Validate(1, values, new UiPolicy()));

        Assert.Contains(nodeId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("node", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Positive_property_node_id_remains_valid()
    {
        var values = ImmutableArray.Create(
            new UiPropertyValue(1, UiPropertyId.Text, UiValue.FromString("x")));

        UiMaterializationValidator.Validate(1, values, new UiPolicy());
    }
}
