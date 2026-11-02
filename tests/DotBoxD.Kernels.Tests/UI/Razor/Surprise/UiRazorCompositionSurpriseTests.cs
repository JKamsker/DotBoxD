using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorCompositionSurpriseTests
{
    private const string Component = """
        using DotBoxD.UI.Authoring;
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

    [Fact]
    public void Attributed_partial_components_are_available_before_their_interfaces_are_generated()
    {
        const string source = """
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Child;
            """;

        var result = Generate(source, """<UiComponent Type="Child" />""");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("Child")]
    [InlineData("Outer.Child")]
    public void Concrete_interface_compositions_resolve_as_the_generated_C_sharp_will(string type)
    {
        const string source = """
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter;
            public struct Child : IUiComponent
            {
                public UiElement Render(UiBuilder builder) => builder.Text("child");
            }
            public static class Outer
            {
                public sealed class Child : IUiComponent
                {
                    public UiElement Render(UiBuilder builder) => builder.Text("child");
                }
            }
            """;

        var result = Generate(source, "<UiComponent Type=\"" + type + "\" />");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
