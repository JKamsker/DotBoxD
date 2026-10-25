using System.Collections.Immutable;
using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiMaterializationValidatorArgumentValidationTests
{
    [Theory]
    [MemberData(nameof(Values))]
    public void Null_policy_is_rejected_at_the_materialization_boundary(ImmutableArray<UiPropertyValue> values)
    {
        var exception = Assert.Throws<ArgumentNullException>(() => UiMaterializationValidator.Validate(0, values, null!));

        Assert.Equal("policy", exception.ParamName);
    }

    public static IEnumerable<object[]> Values()
    {
        yield return [ImmutableArray<UiPropertyValue>.Empty];
        yield return [ImmutableArray.Create(new UiPropertyValue(1, UiPropertyId.Text, UiValue.FromString("value")))];
    }
}
