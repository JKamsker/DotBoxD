using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorResourceHandleSurpriseTests
{
    [Fact]
    public void Resource_declaration_rejects_URLs_instead_of_deferring_them_to_runtime()
    {
        var result = Generate(Component, """
            @resource icon = "https://example.test/image";
            <UiImage Resource="@icon" />
            """);

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Resource_declaration_accepts_host_granted_opaque_handles()
    {
        var result = Generate(Component, """
            @resource icon = "host.icon";
            <UiImage Resource="@icon" />
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.Contains(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
