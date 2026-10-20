using System.Text.Json;
using DotBoxD.UI.Authoring;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotBoxD.UI.Blazor.Tests.Resources;

public sealed class BlazorExtensionTests
{
    [Fact]
    public async Task Registered_policy_granted_extension_renders_only_trusted_host_code()
    {
        var builder = new UiBuilder();
        var policy = new UiPolicy { AllowedExtensionSchemas = ["test.badge"] };
        var package = builder.Build(builder.Extension("test.badge", "{\"text\":\"<script>\"}"), policy);
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer(extensions: [new Badge()]);
        await using var session = await UiFixture.Host(sandbox, policy).InstallAsync(package, renderer);
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var html = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var output = await html.Dispatcher.InvokeAsync(async () =>
            (await html.RenderComponentAsync<DotBoxDUi>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(DotBoxDUi.Session)] = session }))).ToHtmlString());
        Assert.Contains("<strong>&lt;script&gt;</strong>", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("registration")]
    [InlineData("schema")]
    [InlineData("limit")]
    [InlineData("duplicates")]
    public async Task Unregistered_disabled_invalid_and_over_limit_extensions_fail_closed(string failure)
    {
        var builder = new UiBuilder();
        var policy = new UiPolicy { AllowedExtensionSchemas = ["test.badge"] };
        var package = builder.Build(builder.Extension("test.badge", "{\"text\":\"ok\"}"), policy);
        if (failure == "policy")
        { policy = policy with { AllowedExtensionSchemas = [] }; }
        if (failure == "limit")
        { policy = policy with { MaxExtensionBytes = 1 }; }
        if (failure == "schema")
        { package = package with { Extensions = [new(1, "test.badge", "{\"html\":\"<script>\"}")] }; }
        if (failure == "duplicates")
        { package = package with { Extensions = [new(1, "test.badge", "{\"text\":\"ok\",\"text\":\"bad\"}")] }; }
        var renderer = new BlazorUiRenderer(extensions: failure == "registration" ? [] : [new Badge()]);
        using var sandbox = UiFixture.Sandbox();
        await Assert.ThrowsAsync<UiValidationException>(async () => await UiFixture.Host(sandbox, policy).InstallAsync(package, renderer));
        Assert.Equal(0, renderer.Materializations);
        Assert.True(renderer.IsDisposed);
    }

    private sealed class Badge : IUiBlazorExtension
    {
        public string SchemaId => "test.badge";
        public void Validate(JsonElement payload)
        {
            if (payload.EnumerateObject().Count() != 1 || !payload.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String || text.GetString()!.Length > 64)
            { throw new UiValidationException("Bad badge schema."); }
        }
        public void Render(RenderTreeBuilder builder, JsonElement payload)
        {
            builder.OpenElement(0, "strong");
            builder.AddContent(1, payload.GetProperty("text").GetString());
            builder.CloseElement();
        }
    }
}
