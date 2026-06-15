using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class PluginLiveSettingRuntimeTypeTests
{
    [Fact]
    public void BindValue_rejects_enum_live_setting_type()
    {
        var server = DotBoxD.Plugins.PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(() =>
            server.BindValue("Mode", DayOfWeek.Monday));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK020");
    }

    [Fact]
    public void BindContext_rejects_nullable_live_setting_type()
    {
        var server = DotBoxD.Plugins.PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(() =>
            server.BindContext<INullableSettings>("settings"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK020");
    }

    private interface INullableSettings
    {
        int? Limit { get; set; }
    }
}
