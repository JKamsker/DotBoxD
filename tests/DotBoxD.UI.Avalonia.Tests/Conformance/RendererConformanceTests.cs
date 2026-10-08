using Avalonia.Headless.XUnit;
using DotBoxD.UI.Conformance;

namespace DotBoxD.UI.Avalonia.Tests.Conformance;

public sealed class RendererConformanceTests
{
    [AvaloniaFact]
    public async Task Avalonia_obeys_the_shared_semantic_contract()
    {
        var renderer = new AvaloniaUiRenderer();
        await UiRendererConformance.RunAsync(renderer);
        Assert.Equal(1, renderer.Materializations);
        Assert.True(renderer.IsDisposed);
    }
}
