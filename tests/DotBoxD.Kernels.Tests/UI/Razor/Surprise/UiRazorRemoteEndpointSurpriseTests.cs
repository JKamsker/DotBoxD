using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorRemoteEndpointSurpriseTests
{
    [Fact]
    public void Unresolved_remote_endpoint_constant_gets_a_focused_diagnostic()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter
            {
            }
            """, """
            <UiButton Text="Go" OnClick="@Ui.Remote(MissingEndpoint)" />
            """);

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Component_qualified_remote_endpoint_constant_is_accepted()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter
            {
                [UiRemoteHandler(7)] public static void Search() { }
            }
            """, """
            <UiButton Text="Go" OnClick="@Ui.Remote(Counter.SearchUiEndpoint)" />
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Unrelated_component_qualified_endpoint_constant_gets_a_focused_diagnostic()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter
            {
                [UiRemoteHandler(7)] public static void Search() { }
            }
            """, """
            <UiButton Text="Go" OnClick="@Ui.Remote(Other.SearchUiEndpoint)" />
            """);

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Zero_valued_remote_endpoint_constant_gets_a_focused_diagnostic()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter { public const int Endpoint = 0; }
            """, """
            <UiButton Text="Go" OnClick="@Ui.Remote(Endpoint)" />
            """);

        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXR001"));
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Inherited_remote_handler_endpoint_constant_is_accepted()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            public partial class Base { [UiRemoteHandler(7)] public static void Search() { } }
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter : Base { }
            """, """
            <UiButton Text="Go" OnClick="@Ui.Remote(SearchUiEndpoint)" />
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Error_stub_preserves_the_component_contract()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            [UiRazorComponent("Counter.ui.razor")]
            public partial class Counter { }
            """, """
            <UiButton Text="Go" OnClick="@Ui.Remote(MissingEndpoint)" />
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXR001");
        Assert.Contains(" : global::DotBoxD.UI.Authoring.IUiComponent", string.Join("", result.Result.GeneratedTrees), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
