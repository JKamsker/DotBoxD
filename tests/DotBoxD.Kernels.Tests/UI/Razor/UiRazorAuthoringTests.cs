using DotBoxD.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static DotBoxD.Kernels.Tests.UI.Razor.UiRazorFixture;

namespace DotBoxD.Kernels.Tests.UI.Razor;

public sealed class UiRazorAuthoringTests
{
    [Fact]
    public async Task Safe_Razor_and_handwritten_public_builder_have_identical_hash_and_semantics()
    {
        var generated = Package();
        var handwrittenSource = Component.Replace("[UiRazorComponent(\"Counter.ui.razor\")]", "", StringComparison.Ordinal)
            .Replace("public partial class Counter", "public partial class Counter : IUiComponent", StringComparison.Ordinal)
            .Replace("[UiLocalHandler] public static int Increment", """
                public UiElement Render(UiBuilder builder)
                {
                    var count = builder.State(0);
                    var label = builder.Kernel(LabelUiKernel(), count);
                    var increment = builder.Kernel(IncrementUiKernel(), count);
                    return builder.Stack(builder.Text(label), builder.Button("Increment", increment, count));
                }
                [UiLocalHandler] public static int Increment
                """, StringComparison.Ordinal);
        var handwritten = Package(handwrittenSource);
        Assert.Equal(UiPackageJson.ComputeHash(generated, new UiPolicy()), UiPackageJson.ComputeHash(handwritten, new UiPolicy()));
        using var sandbox = UiTestFixture.Sandbox();
        await using var first = await UiTestFixture.Host(sandbox).InstallAsync(generated, new RecordingUiRenderer());
        await using var second = await UiTestFixture.Host(sandbox).InstallAsync(handwritten, new RecordingUiRenderer());
        Assert.Equal((await first.DispatchAsync(1)).State.ToArray(), (await second.DispatchAsync(1)).State.ToArray());
    }

    [Fact]
    public void Typed_inputs_conditionals_keyed_items_resources_and_remote_routes_lower_to_shared_schema()
    {
        var package = Package(markup: """
            @state string query = "";
            @state bool shown = true;
            @state double progress = 10;
            @state items rows = [];
            @resource icon = "host.icon";
            <UiWhen Condition="@Ui.Bind(shown)">
                <UiGrid Columns="2">
                    <UiTextBox Text="@Ui.TwoWay(query)" />
                    <UiCheckBox Checked="@Ui.TwoWay(shown)" />
                    <UiSlider Value="@Ui.TwoWay(progress)" />
                    <UiProgressBar Value="@Ui.Bind(progress)" />
                    <UiItems Items="@Ui.Bind(rows)" @key="Key" />
                    <UiButton Text="Search" OnClick="@Ui.Remote(SearchUiEndpoint)" />
                    <UiImage Resource="@icon" Text="&lt;script&gt;" />
                </UiGrid>
            </UiWhen>
            """);
        Assert.Equal(7, Assert.Single(package.Events).RemoteEndpointId);
        Assert.Equal(3, package.Nodes.SelectMany(n => n.Properties).Count(p => p.TwoWay));
        Assert.Contains(package.Nodes, n => n.Properties.Any(p => p.Id == UiPropertyId.Visible));
        Assert.Contains(package.Nodes, n => n.Primitive == UiPrimitive.Items);
        Assert.Equal("host.icon", Assert.Single(package.Resources).Handle);
    }

    [Theory]
    [InlineData("@state bool flag = -true;\n<UiText />")]
    [InlineData("<UiText Enabled=\"@Ui.TwoWay(flag)\" />")]
    [InlineData("<UiHorizontal Horizontal=\"true\" />")]
    [InlineData("<div />")]
    [InlineData("<DynamicComponent Type=\"@typeof(Counter)\" />")]
    [InlineData("<UiText Text=\"@new Microsoft.AspNetCore.Components.MarkupString(\"x\")\" />")]
    [InlineData("<UiButton onclick=\"@Run\" />")]
    [InlineData("<UiText style=\"position:fixed\" />")]
    [InlineData("<UiText Text=\"@Ui.Bind(System.IO.File.ReadAllText(\"x\"))\" />")]
    [InlineData("<UiVertical>@foreach(var x in rows) { <UiText /> }</UiVertical>")]
    [InlineData("<UiItems @key=\"@Guid.NewGuid()\" />")]
    [InlineData("<!DOCTYPE x [<!ENTITY leak SYSTEM 'file:///etc/passwd'>]><UiText Text=\"&leak;\" />")]
    [InlineData("@inject IJSRuntime JS\n<UiText />")]
    [InlineData("@code { void Run() {} }\n<UiText />")]
    [InlineData("<UiImage Resource=\"https://example.test/x.png\" />")]
    public void Escape_hatches_get_deterministic_actionable_diagnostics(string markup)
    {
        var first = Generate(Component, markup);
        var second = Generate(Component, markup);
        var error = Assert.Single(first.Diagnostics.Where(d => d.Id == "DBXR001"));
        Assert.Equal(error.GetMessage(), Assert.Single(second.Diagnostics.Where(d => d.Id == "DBXR001")).GetMessage());
        Assert.NotEqual(Location.None, error.Location);
        Assert.DoesNotContain(first.Result.GeneratedTrees, t => t.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Referenced_local_methods_use_existing_lowerer_and_never_fall_back_to_worker_execution()
    {
        var source = Component.Replace("value + 1", "System.IO.File.ReadAllText(\"x\").Length", StringComparison.Ordinal);
        Assert.Contains(Generate(source, Markup).Diagnostics, d => d.Id == "DBXU001");
    }

    [Theory]
    [InlineData("GenerateRender = false")]
    [InlineData("")]
    public void Handwritten_Render_wins_and_render_generation_can_be_disabled(string optOut)
    {
        var source = Component.Replace("\"Counter.ui.razor\")", "\"Counter.ui.razor\"" + (optOut.Length > 0 ? ", " + optOut : "") + ")", StringComparison.Ordinal)
            .Replace("[UiLocalHandler] public static int Increment", "public UiElement Render(UiBuilder builder) => builder.Text(\"manual\"); [UiLocalHandler] public static int Increment", StringComparison.Ordinal);
        var result = Generate(source, "<escape />");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXR001");
        Assert.DoesNotContain(result.Result.GeneratedTrees, t => t.FilePath.EndsWith("UiRazor.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Unrelated_CSharp_edits_preserve_cached_Razor_output()
    {
        var initial = Generate(Component, Markup);
        var changed = initial.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal class Unrelated {}"));
        var driver = initial.Driver.RunGeneratorsAndUpdateCompilation(changed, out _, out _);
        var steps = driver.GetRunResult().Results[1].TrackedSteps["UiRazorComponents"];
        Assert.All(steps.SelectMany(s => s.Outputs), o => Assert.Contains(o.Reason,
            new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged }));
    }
}
