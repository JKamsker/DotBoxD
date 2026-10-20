using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotBoxD.UI.Blazor.Tests.Resources;

public sealed class BlazorResourceTests
{
    [Fact]
    public async Task Granted_pixels_render_as_bounded_PNG_and_escaped_alternative_text()
    {
        var builder = new UiBuilder();
        var package = builder.Build(builder.Image(builder.Resource("test.icon"), "<script>alert(1)</script>"));
        var rgba = new byte[] { 10, 20, 30, 255 };
        var image = UiImageResource.FromRgba(1, 1, rgba);
        rgba[0] = 0;
        var catalog = new UiResourceCatalog(new Dictionary<string, UiImageResource>(StringComparer.Ordinal) { ["test.icon"] = image });
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer(resources: catalog);
        await using var session = await UiFixture.Host(sandbox).InstallAsync(package, renderer);
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var html = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var output = await html.Dispatcher.InvokeAsync(async () =>
            (await html.RenderComponentAsync<DotBoxDUi>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(DotBoxDUi.Session)] = session }))).ToHtmlString());
        Assert.Contains("src=\"data:image/png;base64,", output, StringComparison.Ordinal);
        Assert.Contains("width=\"1\" height=\"1\"", output, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", output, StringComparison.Ordinal);
        Assert.Equal(10, image.Rgba[0]);
        Assert.Equal(1, renderer.Materializations);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("pixels")]
    [InlineData("bytes")]
    [InlineData("total")]
    public async Task Missing_grants_and_resource_quotas_fail_before_materialization(string failure)
    {
        var builder = new UiBuilder();
        var package = builder.Build(builder.Image(builder.Resource("test.icon")));
        var image = UiImageResource.FromRgba(2, 2, new byte[16]);
        var catalog = failure == "missing" ? UiResourceCatalog.Empty : new UiResourceCatalog(
            new Dictionary<string, UiImageResource>(StringComparer.Ordinal) { ["test.icon"] = image });
        var policy = failure switch
        {
            "pixels" => new UiPolicy { MaxImagePixels = 1 },
            "bytes" => new UiPolicy { MaxResourceBytes = 4 },
            "total" => new UiPolicy { MaxTotalResourceBytes = 4 },
            _ => new UiPolicy()
        };
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer(resources: catalog);
        await Assert.ThrowsAsync<UiValidationException>(async () => await UiFixture.Host(sandbox, policy).InstallAsync(package, renderer));
        Assert.Equal(0, renderer.Materializations);
        Assert.True(renderer.IsDisposed);
    }
}
