using System.Globalization;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class ParameterReaderDecimalConversionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decimal_parameters_preserve_invariant_precision_scale_and_single_getter_evaluation(bool nullable)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            DecimalParameters parameters = nullable ? new NullableParameters() : new RequiredParameters();
            var policy = SandboxPolicyBuilder.Create().Grant("custom.parameters", parameters).Build();
            var grant = Assert.Single(policy.Grants);

            Assert.Equal("12345678901234567890.123456789", grant.Parameters["Value"]);
            Assert.Equal("-79228162514264337593543950335", grant.Parameters["Minimum"]);
            Assert.Equal("79228162514264337593543950335", grant.Parameters["Maximum"]);
            Assert.Equal("1.2300", grant.Parameters["Scaled"]);
            Assert.Equal(1, parameters.Reads);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private abstract class DecimalParameters
    {
        public int Reads;
        public decimal Minimum => decimal.MinValue;
        public decimal Maximum => decimal.MaxValue;
        public decimal Scaled => 1.2300m;

        protected decimal Next() => Reads++ == 0 ? 12345678901234567890.123456789m : 0m;
    }

    private sealed class RequiredParameters : DecimalParameters
    {
        public decimal Value => Next();
    }

    private sealed class NullableParameters : DecimalParameters
    {
        public decimal? Value => Next();
    }
}
