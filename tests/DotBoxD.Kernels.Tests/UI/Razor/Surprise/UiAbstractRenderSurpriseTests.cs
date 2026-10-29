using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiAbstractRenderSurpriseTests
{
    [Fact]
    public void Abstract_inherited_Render_is_generated_or_rejected_with_a_focused_diagnostic()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            public abstract class BaseComponent
            {
                public abstract UiElement Render(UiBuilder builder);
            }
            [UiRazorComponent("Counter.ui.razor")]
            public abstract partial class Counter : BaseComponent;
            """, "<UiText Text=\"generated\" />");

        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.True(
            result.Diagnostics.Any(d => d.Id == "DBXR001") ||
            result.Result.GeneratedTrees.Any(tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal)),
            "An abstract Render(UiBuilder) must not silently suppress an opted-in Safe Razor component.");
    }
}
