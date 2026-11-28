using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorOrientationSurpriseTests
{
    [Fact]
    public void UiVertical_rejects_a_contradictory_horizontal_orientation()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter;
            """, """
            <UiVertical Horizontal="true"><UiText Text="x" /></UiVertical>
            """);

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("<UiVertical><UiText Text=\"x\" /></UiVertical>")]
    [InlineData("<UiStack Horizontal=\"true\"><UiText Text=\"x\" /></UiStack>")]
    public void Noncontradictory_stack_orientations_remain_supported(string markup)
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter;
            """, markup);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.Contains(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
