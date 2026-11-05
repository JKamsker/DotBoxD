using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorRemoteEndpointSurpriseTests
{
    [Fact]
    public void Unresolved_remote_endpoint_constant_gets_a_focused_diagnostic()
    {
        var result = Generate(Component, """
            <UiButton Text="Go" OnClick="@Ui.Remote(MissingEndpoint)" />
            """);

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
