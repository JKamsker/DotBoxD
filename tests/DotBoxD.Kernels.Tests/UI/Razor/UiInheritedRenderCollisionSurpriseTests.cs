using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiInheritedRenderCollisionSurpriseTests
{
    [Theory]
    [InlineData("public void")]
    [InlineData("protected void")]
    [InlineData("public static void")]
    [InlineData("protected static void")]
    public void Incompatible_inherited_Render_gets_actionable_diagnostic(string signature)
    {
        var source = $$"""
            using DotBoxD.UI.Authoring;
            public class Base { {{signature}} Render(UiBuilder builder) { } }
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter : Base;
            """;
        var result = Generate(source, Markup);

        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(result.Diagnostics, d => d.Id == "DBXR001");
        Assert.DoesNotContain(result.Result.GeneratedTrees, t => t.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(), d => d.Id == "CS0108");
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compatible_explicit_Render_wins_independently_of_declaration_order(bool explicitFirst)
    {
        const string manual = "UiElement IUiComponent.Render(UiBuilder builder) => builder.Text(\"manual\");";
        const string collision = "public static void Render(UiBuilder builder) { }";
        var result = Generate($$"""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter : IUiComponent
            {
                {{(explicitFirst ? manual + collision : collision + manual)}}
            }
            """, "<escape />");
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.DoesNotContain(result.Result.GeneratedTrees, t => t.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public string Render = \"manual\";")]
    [InlineData("public event System.Action Render { add { } remove { } }")]
    [InlineData("public sealed class Render { }")]
    public void Nonmethod_Render_collisions_get_a_focused_diagnostic(string member)
    {
        var result = Generate($$"""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter { {{member}} }
            """, "<UiText />");
        Assert.Contains(result.Diagnostics, d => d.Id == "DBXR001");
        Assert.DoesNotContain(result.Result.GeneratedTrees, t => t.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(), d => d.Id == "CS0102");
    }

    [Fact]
    public void Inherited_explicit_interface_Render_wins_over_derived_static_helper()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            public class Base : IUiComponent
            {
                UiElement IUiComponent.Render(UiBuilder builder) => builder.Text("manual");
            }
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter : Base { public static void Render(UiBuilder builder) { } }
            """, "<escape />");
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.DoesNotContain(result.Result.GeneratedTrees, t => t.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public string Render = \"manual\";", true)]
    [InlineData("protected string Render => \"manual\";", true)]
    [InlineData("public event System.Action Render { add { } remove { } }", true)]
    [InlineData("public sealed class Render { }", true)]
    [InlineData("private string Render => \"manual\";", false)]
    public void Inherited_nonmethod_Render_collision_respects_accessibility(string member, bool collision)
    {
        var result = Generate($$"""
            using DotBoxD.UI.Authoring;
            public class Base { {{member}} }
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter : Base;
            """, Markup);
        Assert.Equal(collision, result.Diagnostics.Any(d => d.Id == "DBXR001"));
        Assert.DoesNotContain(result.Output.GetDiagnostics(), d => d.Id == "CS0108");
    }

}
