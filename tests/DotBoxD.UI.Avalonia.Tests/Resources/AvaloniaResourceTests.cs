using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia.Tests.Resources;

public sealed class AvaloniaResourceTests
{
    [AvaloniaFact]
    public async Task Host_pixels_materialize_once_and_are_retained_during_state_only_updates()
    {
        var builder = new UiBuilder();
        var alt = builder.State("before");
        var resource = builder.Resource("test.icon");
        var package = builder.Build(builder.Element(UiPrimitive.Image,
            [UiLiteral.Integer(resource).Property(UiPropertyId.Resource), ((UiBinding<string>)alt).Property(UiPropertyId.Text)]));
        var catalog = new UiResourceCatalog(new Dictionary<string, UiImageResource>(StringComparer.Ordinal)
        { ["test.icon"] = UiImageResource.FromRgba(1, 1, [10, 20, 30, 255]) });
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        var renderer = new AvaloniaUiRenderer(resources: catalog);
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build()).InstallAsync(package, renderer);
        var image = Assert.IsType<Image>(renderer.Root);
        var source = image.Source;
        Assert.NotNull(source);
        await session.ApplyPatchAsync(new UiStatePatch(session.Id, 0, [new(alt.Id, UiValue.FromString("after"))]));
        Assert.Same(source, image.Source);
        Assert.Equal("after", global::Avalonia.Automation.AutomationProperties.GetName(image));
        Assert.Equal(1, renderer.Materializations);
        await session.DisposeAsync();
        Assert.Null(renderer.Root);
    }
}
