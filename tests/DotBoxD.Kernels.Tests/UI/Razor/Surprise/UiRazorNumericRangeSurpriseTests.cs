using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorNumericRangeSurpriseTests
{
    [Fact]
    public void Out_of_range_numeric_literal_gets_a_focused_diagnostic()
    {
        var result = Generate(Component, "<UiSlider Value=\"-1\" Maximum=\"100\" />");

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
