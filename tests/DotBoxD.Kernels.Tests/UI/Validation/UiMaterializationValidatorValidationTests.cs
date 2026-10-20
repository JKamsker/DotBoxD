using System.Collections.Immutable;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiMaterializationValidatorValidationTests
{
    [Fact]
    public void Null_property_entries_fail_closed_at_the_materialization_boundary()
    {
        Assert.Throws<UiValidationException>(() =>
            UiMaterializationValidator.Validate(0, ImmutableArray.CreateRange<UiPropertyValue>([null!]), new UiPolicy()));
    }
}
