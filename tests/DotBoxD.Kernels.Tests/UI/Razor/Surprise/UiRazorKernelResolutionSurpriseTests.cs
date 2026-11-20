using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorKernelResolutionSurpriseTests
{
    [Fact]
    public void Unresolved_local_kernel_handler_gets_a_focused_markup_diagnostic()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter;
            """, """
            @state int count = 0;
            @kernel missing = Ui.Bind(Missing, count);
            <UiText Text="@Ui.Bind(missing)" />
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.Contains("Missing", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("Counter.ui.razor", Path.GetFileName(diagnostic.Location.GetLineSpan().Path));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
