using System.Globalization;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Policies;
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
        AssertAllocation(kind.ToString(), parameters, format);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nullable_decimal_conversion_avoids_boxing(bool hasValue)
    {
        decimal? value = hasValue ? 42.5m : null;
        AssertAllocation(
            $"NullableDecimal, hasValue={hasValue}",
            new ParameterValue<decimal?>(value),
            () => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private void AssertAllocation(string name, object parameters, Func<string> format)
    {
        var grant = Assert.Single(SandboxPolicyBuilder.Create().Grant("custom.parameters", parameters).Build().Grants);
        Assert.Equal(format(), grant.Parameters["Value"]);
        var textParameters = new ParameterValue<string>(string.Empty);
        _ = Measure(parameters, textParameters, format, useText: false);
        _ = Measure(parameters, textParameters, format, useText: true);
        var primitiveBytes = Measure(parameters, textParameters, format, useText: false);
        var textBytes = Measure(parameters, textParameters, format, useText: true);
        output.WriteLine($"{name}: primitive {primitiveBytes / 1000D} B/grant; text control {textBytes / 1000D} B/grant");
        Assert.True(primitiveBytes <= textBytes, $"Primitive conversion allocated {primitiveBytes - textBytes} extra bytes.");
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long Measure(object parameters, ParameterValue<string> textParameters, Func<string> format, bool useText)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            if (useText)
            {
                textParameters.Value = format();
            }

            var policy = SandboxPolicyBuilder.Create().Grant("custom.parameters", useText ? textParameters : parameters).Build();
            GC.KeepAlive(policy);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static (object Parameters, Func<string> Format) CreateCase(TypeCode kind)
        => kind switch
        {
            TypeCode.Boolean => (new ParameterValue<bool>(true), static () => Convert.ToString(true, CultureInfo.InvariantCulture)),
            TypeCode.Int32 => (new ParameterValue<int>(42), static () => Convert.ToString(42, CultureInfo.InvariantCulture)),
            TypeCode.Int64 => (new ParameterValue<long>(42), static () => Convert.ToString(42L, CultureInfo.InvariantCulture)),
            TypeCode.Double => (new ParameterValue<double>(42.5), static () => Convert.ToString(42.5, CultureInfo.InvariantCulture)),
            TypeCode.Decimal => (new ParameterValue<decimal>(42.5m), static () => Convert.ToString(42.5m, CultureInfo.InvariantCulture)),
            TypeCode.DateTime => (new ParameterValue<DateTime>(DateTime.UnixEpoch), static () => Convert.ToString(DateTime.UnixEpoch, CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private sealed class ParameterValue<T>(T value)
    {
        public T Value { get; set; } = value;
    }
}
