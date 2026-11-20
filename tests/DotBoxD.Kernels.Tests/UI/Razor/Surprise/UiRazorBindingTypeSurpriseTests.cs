using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorBindingTypeSurpriseTests
{
    [Fact]
    public void State_bound_to_an_incompatible_property_gets_a_focused_diagnostic()
    {
        var result = Generate(Component, """
            @state int count = 0;
            <UiText Enabled="@Ui.Bind(count)" />
            """);

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void State_bound_to_a_matching_property_type_generates_cleanly()
    {
        var result = Generate(Component, """
            @state bool enabled = true;
            <UiText Enabled="@Ui.Bind(enabled)" />
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.Contains(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
