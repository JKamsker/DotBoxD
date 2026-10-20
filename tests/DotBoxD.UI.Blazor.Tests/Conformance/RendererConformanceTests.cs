using System.Collections.Immutable;
using DotBoxD.UI.Conformance;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Conformance;

public sealed class RendererConformanceTests
{
    [Fact]
    public async Task Blazor_obeys_the_shared_semantic_contract()
    {
        var renderer = new BlazorUiRenderer();
        await UiRendererConformance.RunAsync(renderer);
        Assert.Equal(1, renderer.Materializations);
        Assert.True(renderer.IsDisposed);
    }

    [Fact]
    public Task Fake_obeys_the_shared_semantic_contract() => UiRendererConformance.RunAsync(new FakeRenderer());

    private sealed class FakeRenderer : IUiRenderer
    {
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
