using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class LiveValueFiniteValueSurpriseTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Constructor_rejects_non_finite_double_values(double value)
    {
        var exception = Assert.Throws<SandboxValidationException>(
            () => new LiveValue<double>("Multiplier", value));

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Typed_value_setter_rejects_non_finite_double_without_publishing_value(double value)
    {
        var setting = new LiveValue<double>("Multiplier", 1D);

        var exception = Assert.Throws<SandboxValidationException>(() => setting.Value = value);

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
        Assert.Equal(1D, setting.Value);
        Assert.Equal(1D, setting.CurrentValue);
    }

    [Fact]
    public void Finite_constructor_and_typed_value_setter_values_remain_usable()
    {
        var setting = new LiveValue<double>("Multiplier", 1D);

        setting.Value = 2D;

        Assert.Equal(2D, setting.Value);
        Assert.Equal(2D, setting.CurrentValue);
    }
}
