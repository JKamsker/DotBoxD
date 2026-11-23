using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorNumericRangeSurpriseTests
{
    [Theory]
    [InlineData("Value", "-1")]
    [InlineData("Value", "1000000000.1")]
    [InlineData("Value", "2147483648")]
    [InlineData("Maximum", "0")]
    [InlineData("Maximum", "1000000000.1")]
    public void Out_of_range_numeric_literal_gets_a_focused_diagnostic(string property, string value)
    {
        var otherProperty = property == "Value" ? "Maximum=\"100\"" : "Value=\"0\"";
        var result = Generate(Component, "<UiSlider " + property + "=\"" + value + "\" " + otherProperty + " />");

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void In_range_numeric_literal_remains_a_clean_control()
    {
        var result = Generate(Component, "<UiSlider Value=\"0\" Maximum=\"100\" />");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.Contains(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
