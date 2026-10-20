using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia.Tests.Resources;

public sealed class AvaloniaExtensionTests
{
    [AvaloniaFact]
    public async Task Extension_requires_host_registration_and_policy_and_is_released_on_disposal()
    {
        var builder = new UiBuilder();
        var policy = new UiPolicy { AllowedExtensionSchemas = ["test.badge"] };
        var package = builder.Build(builder.Extension("test.badge", "{\"text\":\"hello\"}"), policy);
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        var host = new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(), policy);
        var missing = new AvaloniaUiRenderer();
        await Assert.ThrowsAsync<UiValidationException>(async () => await host.InstallAsync(package, missing));
        Assert.True(missing.IsDisposed);
        var renderer = new AvaloniaUiRenderer(extensions: [new Badge()]);
        await using var session = await host.InstallAsync(package, renderer);
        Assert.Equal("hello", Assert.IsType<TextBlock>(renderer.Root).Text);
        await session.DisposeAsync();
        Assert.Null(renderer.Root);
    }

    [AvaloniaFact]
    public async Task Extension_disposal_during_creation_fails_without_publishing_a_session()
    {
        var builder = new UiBuilder();
        var policy = new UiPolicy { AllowedExtensionSchemas = ["test.dispose"] };
        var package = builder.Build(builder.Extension("test.dispose", "{}"), policy);
        var extension = new DisposeDuringCreate();
        var renderer = new AvaloniaUiRenderer(extensions: [extension]);
        extension.Renderer = renderer;
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        var host = new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(), policy);

        UiSession? session = null;
        Exception? error = null;
        try
        { session = await host.InstallAsync(package, renderer); }
        catch (Exception exception)
        { error = exception; }
        await extension.Disposal.WaitAsync(TestContext.Current.CancellationToken);
        if (session is not null)
        { await session.DisposeAsync(); }

        Assert.Null(session);
        Assert.IsType<ObjectDisposedException>(error);
        Assert.True(renderer.IsDisposed);
        Assert.Null(renderer.Root);
        Assert.Equal(0, renderer.Materializations);
    }

    private sealed class Badge : IUiAvaloniaExtension
    {
        public string SchemaId => "test.badge";
        public void Validate(JsonElement payload)
        {
            if (payload.EnumerateObject().Count() != 1 || !payload.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String || text.GetString()!.Length > 64)
            { throw new UiValidationException("Bad badge schema."); }
        }
        public Control Create(JsonElement payload) => new TextBlock { Text = payload.GetProperty("text").GetString() };
    }

    private sealed class DisposeDuringCreate : IUiAvaloniaExtension
    {
        public string SchemaId => "test.dispose";
        public AvaloniaUiRenderer Renderer { private get; set; } = null!;
        public Task Disposal { get; private set; } = Task.CompletedTask;
        public void Validate(JsonElement payload) { }
        public Control Create(JsonElement payload)
        {
            Disposal = Renderer.DisposeAsync().AsTask();
            return new TextBlock();
        }
    }
}
