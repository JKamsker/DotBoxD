using System.Collections.Immutable;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI.Validation;

public sealed class UiMaterializationValidatorDefaultValuesTests
{
    [Fact]
    public void Default_values_fail_closed_while_empty_values_are_valid()
    {
        Assert.Throws<UiValidationException>(() => UiMaterializationValidator.Validate(0, default, new UiPolicy()));

        UiMaterializationValidator.Validate(0, ImmutableArray<UiPropertyValue>.Empty, new UiPolicy());
    }
}
