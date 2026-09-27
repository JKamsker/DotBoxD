using System.Globalization;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Policy;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ParameterReaderFormattableAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Guid", false, true)]
    [InlineData("Guid", true, true)]
    [InlineData("Guid", true, false)]
    [InlineData("DateTimeOffset", false, true)]
    [InlineData("DateTimeOffset", true, true)]
    [InlineData("DateTimeOffset", true, false)]
    [InlineData("Custom", false, true)]
    [InlineData("Custom", true, true)]
    [InlineData("Custom", true, false)]
    public void Value_type_formatting_does_not_add_boxing(string shape, bool nullable, bool hasValue)
    {
        var (parameters, format) = shape switch
        {
            "Guid" => CreateCase(Guid.Empty, nullable, hasValue, static value => value.ToString(null, CultureInfo.InvariantCulture)),
            "DateTimeOffset" => CreateCase(DateTimeOffset.UnixEpoch, nullable, hasValue, static value => value.ToString(null, CultureInfo.InvariantCulture)),
            "Custom" => CreateCase(new FormattableValue(42.5m), nullable, hasValue, static value => value.ToString(null, CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        ParameterReaderAllocationFixture.AssertAllocation(
            output, $"{shape}, nullable={nullable}, hasValue={hasValue}", parameters, format);
    }

    private static (object Parameters, Func<string> Format) CreateCase<T>(T value, bool nullable, bool hasValue, Func<T, string> format)
        where T : struct
    {
        T? optional = hasValue ? value : null;
        return nullable
            ? (new PolicyParameterValue<T?>(optional), () => optional.HasValue ? format(optional.Value) : string.Empty)
            : (new PolicyParameterValue<T>(value), () => format(value));
    }

    private readonly record struct FormattableValue(decimal Value) : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);
    }
}
