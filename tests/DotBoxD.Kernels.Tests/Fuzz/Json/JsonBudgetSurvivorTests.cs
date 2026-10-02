using System.Text;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Fuzz.Json;

public sealed class JsonBudgetSurvivorTests
{
    [Fact]
    public void Null_json_is_rejected_before_budget_scanning()
        => Assert.Equal("json", Assert.Throws<ArgumentNullException>(() => JsonImporter.Import(null!)).ParamName);

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDBFF)]
    [InlineData(0xDC00)]
    [InlineData(0xDFFF)]
    public void Truncated_input_ending_in_a_surrogate_reports_invalid_json(int codeUnit)
    {
        var json = "\"" + (char)codeUnit;
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
        Assert.Contains(exception.Diagnostics, d => d.Code == "E-JSON-INVALID");
    }

    [Fact]
    public void Byte_budget_precedes_malformed_text_materialization()
    {
        // An unpaired low surrogate contributes a replacement character's three UTF-8
        // bytes. It must not be counted as a pair with the preceding non-surrogate.
        var invalid = "世" + (char)0xDC00;
        var json = $$"""{"id":"{{invalid}}","version":"1.0.0","functions":[]}""";
        json += new string(' ', 1_048_577 - Encoding.UTF8.GetByteCount(json));
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
        Assert.Contains(exception.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Theory]
    [InlineData("\u007f")]
    [InlineData("\u0080")]
    [InlineData("\u07ff")]
    [InlineData("\u0800")]
    [InlineData("\ud7ff")]
    [InlineData("\ue000")]
    [InlineData("\uffff")]
    [InlineData("🚀")]
    public void Document_limit_uses_exact_utf8_width_at_encoding_boundaries(string text)
    {
        var json = $$"""{"id":"{{text}}","version":"1.0.0","functions":[]}""";
        json += new string(' ', 1_048_576 - Encoding.UTF8.GetByteCount(json));
        Assert.Equal(text, JsonImporter.Import(json).Id);
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json + " "));
        Assert.Contains(exception.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"s\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("0")]
    public void Array_breadth_counts_nested_arrays_strings_and_scalars(string item)
    {
        foreach (var count in new[] { 10000, 10001 })
        {
            var json = "[" + string.Join(',', Enumerable.Repeat(item, count)) + "]";
            var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
            Assert.Contains(exception.Diagnostics, d => d.Code == (count == 10000 ? "E-JSON-TYPE" : "E-JSON-LIMIT"));
        }
    }

    [Theory]
    [InlineData(0xD800, "", "")]
    [InlineData(0xDFFF, "", "")]
    [InlineData(0xD800, "a", "")]
    [InlineData(0xD800, "", "a")]
    [InlineData(0xDFFF, "a", "z")]
    public void Export_rejects_each_unpaired_surrogate_shape(int codeUnit, string prefix, string suffix)
    {
        // Construct at runtime: custom-attribute metadata cannot preserve invalid UTF-16.
        var invalid = prefix + (char)codeUnit + suffix;
        var module = JsonImporter.Import("""{"id":"valid","version":"1.0.0","functions":[]}""");
        var exception = Assert.Throws<SandboxValidationException>(() => JsonExporter.Export(module with { Id = invalid }));
        Assert.Contains(exception.Diagnostics, d => d.Code == "E-JSON-EXPORT");
    }

    [Theory]
    [InlineData("\U00010000")]
    [InlineData("\U0010FFFF")]
    [InlineData("a🚀b🚀c")]
    public void Export_preserves_surrogate_pairs(string valid)
    {
        var module = JsonImporter.Import("""{"id":"valid","version":"1.0.0","functions":[]}""") with { Id = valid };
        Assert.Equal(valid, JsonImporter.Import(JsonExporter.Export(module)).Id);
    }
}
