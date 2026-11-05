using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiNullableRenderSurpriseTests
{
    [Fact]
    public void Nullable_builder_handwritten_Render_is_an_opt_out()
    {
        var source = "#nullable enable\n" + Component.Replace(
            "[UiLocalHandler] public static int Increment",
            "public UiElement Render(UiBuilder? builder) => builder.Text(\"manual\"); " +
            "[UiLocalHandler] public static int Increment", StringComparison.Ordinal);
        var result = Generate(source, "<UiText Text=\"generated\" />");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
