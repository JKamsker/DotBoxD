using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorCrossPropertyRangeSurpriseTests
{
    [Fact]
    public void Value_above_Maximum_reports_a_markup_diagnostic_before_materialization()
    {
        var result = Generate(Component, """<UiSlider Value="100" Maximum="10" />""");

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Value_at_or_below_Maximum_generates_cleanly()
    {
        var result = Generate(Component, """<UiSlider Value="10" Maximum="100" />""");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.Contains(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Negative_Value_at_or_below_Maximum_generates_cleanly()
    {
        var result = Generate(Component, """<UiSlider Value="-1" Maximum="0" />""");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.Contains(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Value_above_negative_Maximum_reports_a_markup_diagnostic_before_materialization()
    {
        var result = Generate(Component, """<UiSlider Value="0" Maximum="-1" />""");

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
    }
}
