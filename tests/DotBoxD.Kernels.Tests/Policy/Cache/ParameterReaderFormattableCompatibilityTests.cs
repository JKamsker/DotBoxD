using System.Globalization;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class ParameterReaderFormattableCompatibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Formatting_reads_once_preserves_copy_semantics_and_uses_invariant_default_format(bool nullable, bool nullResult)
    {
        var probe = new FormatProbe { ReturnNull = nullResult };
        var value = new MutableFormatter(probe);
        object parameters = nullable
            ? new CountingValue<MutableFormatter?>(value, probe)
            : new CountingValue<MutableFormatter>(value, probe);
        var first = Read(parameters);
        var second = Read(parameters);

        Assert.Equal(nullResult ? "" : "42", first);
        Assert.Equal(first, second);
        Assert.Equal(2, probe.GetterCalls);
        Assert.Equal(2, probe.FormatterCalls);
        Assert.Null(probe.Format);
        Assert.Same(CultureInfo.InvariantCulture, probe.Provider);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Convertible_formatting_keeps_precedence_over_formattable(bool nullable)
    {
        object parameters = nullable
            ? new PolicyParameterValue<DualParameterFormatter?>(new DualParameterFormatter())
            : new PolicyParameterValue<DualParameterFormatter>(new DualParameterFormatter());

        Assert.Equal("convertible", Read(parameters));
    }

    [Fact]
    public void Reference_object_and_interface_properties_keep_runtime_formatting()
    {
        var value = new ReferenceFormatter();
        var policy = SandboxPolicyBuilder.Create().Grant("custom.parameters", new
        {
            Reference = value,
            Interface = (IFormattable)value,
            Object = (object)value,
            Null = (IFormattable?)null
        }).Build();
        var values = Assert.Single(policy.Grants).Parameters;

        Assert.Equal("reference", values["Reference"]);
        Assert.Equal("reference", values["Interface"]);
        Assert.Equal("reference", values["Object"]);
        Assert.Empty(values["Null"]);
    }

    private static string Read(object parameters)
        => Assert.Single(SandboxPolicyBuilder.Create().Grant("custom.parameters", parameters).Build().Grants).Parameters["Value"];

    private sealed class FormatProbe
    {
        public int GetterCalls;
        public int FormatterCalls;
        public string? Format;
        public IFormatProvider? Provider;
        public bool ReturnNull;
    }

    private sealed class CountingValue<T>(T value, FormatProbe probe)
    {
        public T Value
        {
            get
            {
                probe.GetterCalls++;
                return value;
            }
        }
    }

    private struct MutableFormatter(FormatProbe probe) : IFormattable
    {
        private int _value = 41;

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            probe.FormatterCalls++;
            probe.Format = format;
            probe.Provider = formatProvider;
            return probe.ReturnNull ? null! : (++_value).ToString(format, formatProvider);
        }
    }

    private sealed class ReferenceFormatter : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "reference";
    }
}
