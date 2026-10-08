using System.Collections.Immutable;
using System.Text.Json.Nodes;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Security;

public sealed class RendererCapabilityTests
{
    [Fact]
    public async Task Required_features_and_primitives_are_negotiated_before_materialization_and_optional_features_may_be_absent()
    {
        using var sandbox = UiFixture.Sandbox();
        var package = UiFixture.TextPackage() with { RequiredFeatures = [UiFeature.TextInput] };
        var missing = new LimitedRenderer(UiRendererCapabilities.Core with { Features = [] });
        await Assert.ThrowsAsync<UiValidationException>(() => UiFixture.Host(sandbox).InstallAsync(package, missing).AsTask());
        Assert.Equal(0, missing.Materializations);
        Assert.True(missing.Disposed);
        var noPrimitives = new LimitedRenderer(UiRendererCapabilities.Core with { Primitives = [] });
        await Assert.ThrowsAsync<UiValidationException>(() => UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), noPrimitives).AsTask());
        var optional = new LimitedRenderer(UiRendererCapabilities.Core with { Features = [] });
        await using var session = await UiFixture.Host(sandbox).InstallAsync(package with { RequiredFeatures = [], OptionalFeatures = [UiFeature.KeyedItems] }, optional);
        Assert.Equal(1, optional.Materializations);
    }

    [Fact]
    public void Feature_declarations_are_bounded_closed_and_canonical()
    {
        var policy = new UiPolicy();
        var package = UiFixture.TextPackage() with { RequiredFeatures = [UiFeature.TextInput, UiFeature.GridLayout] };
        Assert.Equal(UiPackageJson.ComputeHash(package, policy), UiPackageJson.ComputeHash(package with { RequiredFeatures = [UiFeature.GridLayout, UiFeature.TextInput] }, policy));
        var json = UiPackageJson.Export(package, policy);
        Assert.Equal(new[] { UiFeature.GridLayout, UiFeature.TextInput }, UiPackageJson.Import(json, policy).RequiredFeatures);
        foreach (var declaration in new[] { "[999]", "[3,3]", "[1,2,3,4,5,6,7]", "null" })
        {
            var node = JsonNode.Parse(json)!;
            node["requiredFeatures"] = JsonNode.Parse(declaration);
            Assert.Throws<UiValidationException>(() => UiPackageJson.Import(node.ToJsonString(), policy));
        }
        var legacy = JsonNode.Parse(json)!;
        legacy.AsObject().Remove("requiredFeatures");
        legacy.AsObject().Remove("optionalFeatures");
        Assert.Empty(UiPackageJson.Import(legacy.ToJsonString(), policy).RequiredFeatures);
        var raw = JsonNode.Parse(json)!;
        raw["html"] = "<script>alert(1)</script>";
        Assert.Throws<UiValidationException>(() => UiPackageJson.Import(raw.ToJsonString(), policy));
    }

    private sealed class LimitedRenderer(UiRendererCapabilities capabilities) : IUiRenderer
    {
        public UiRendererCapabilities Capabilities => capabilities;
        public int Materializations { get; private set; }
        public bool Disposed { get; private set; }
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
        { Materializations++; return ValueTask.CompletedTask; }
        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
