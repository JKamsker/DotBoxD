using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorCompositionSurpriseTests
{
    private const string Component = """
        using DotBoxD.UI.Razor;
        [UiRazorComponent("Counter.ui.razor")]
        public partial class Counter;
        """;

    [Fact]
    public void Unknown_composition_type_reports_a_markup_diagnostic_before_generated_code_compiles()
    {
        var result = Generate(Component, """<UiComponent Type="MissingComponent" />""");

        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.Contains("MissingComponent", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("Counter.ui.razor", Path.GetFileName(diagnostic.Location.GetLineSpan().Path));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
