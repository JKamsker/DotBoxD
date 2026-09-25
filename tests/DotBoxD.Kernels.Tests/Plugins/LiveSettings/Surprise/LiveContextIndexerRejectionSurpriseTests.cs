using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class LiveContextIndexerRejectionSurpriseTests
{
    [Fact]
    public void BindContext_rejects_indexer_settings_at_materialization()
    {
        using var server = PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.BindContext<IIndexedSettings>("settings"));

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
        Assert.Contains("indexer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Typed_live_setting_view_rejects_indexers_at_materialization()
    {
        var store = new LiveSettingStore([new LiveValue<int>("Value", 1)]);

        var ex = Assert.Throws<SandboxValidationException>(() => store.As<IIndexedSettings>());

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
        Assert.Contains("indexer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ordinary_property_settings_remain_supported()
    {
        using var server = PluginServer.Create();
        var context = server.BindContext<IOrdinarySettings>("settings");

        context.Value.Value = 42;

        Assert.Equal(42, context.Value.Value);
        Assert.Equal(42, context.Settings.Get<int>("Value"));
    }

    private interface IIndexedSettings
    {
        int this[int index] { get; set; }
    }

    private interface IOrdinarySettings
    {
        int Value { get; set; }
    }
}
