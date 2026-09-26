using System.Globalization;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Policy;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ParameterReaderBoxingAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(TypeCode.Boolean)]
    [InlineData(TypeCode.Int32)]
    [InlineData(TypeCode.Int64)]
    [InlineData(TypeCode.Double)]
    [InlineData(TypeCode.Decimal)]
    [InlineData(TypeCode.DateTime)]
    public void Primitive_parameter_conversion_avoids_boxing(TypeCode kind)
    {
        var (parameters, format) = CreateCase(kind);
        ParameterReaderAllocationFixture.AssertAllocation(output, kind.ToString(), parameters, format);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nullable_decimal_conversion_avoids_boxing(bool hasValue)
    {
        decimal? value = hasValue ? 42.5m : null;
        ParameterReaderAllocationFixture.AssertAllocation(
            output,
            $"NullableDecimal, hasValue={hasValue}",
            new PolicyParameterValue<decimal?>(value),
            () => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static (object Parameters, Func<string> Format) CreateCase(TypeCode kind)
        => kind switch
        {
            TypeCode.Boolean => (new PolicyParameterValue<bool>(true), static () => Convert.ToString(true, CultureInfo.InvariantCulture)),
            TypeCode.Int32 => (new PolicyParameterValue<int>(42), static () => Convert.ToString(42, CultureInfo.InvariantCulture)),
            TypeCode.Int64 => (new PolicyParameterValue<long>(42), static () => Convert.ToString(42L, CultureInfo.InvariantCulture)),
            TypeCode.Double => (new PolicyParameterValue<double>(42.5), static () => Convert.ToString(42.5, CultureInfo.InvariantCulture)),
            TypeCode.Decimal => (new PolicyParameterValue<decimal>(42.5m), static () => Convert.ToString(42.5m, CultureInfo.InvariantCulture)),
            TypeCode.DateTime => (new PolicyParameterValue<DateTime>(DateTime.UnixEpoch), static () => Convert.ToString(DateTime.UnixEpoch, CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}
