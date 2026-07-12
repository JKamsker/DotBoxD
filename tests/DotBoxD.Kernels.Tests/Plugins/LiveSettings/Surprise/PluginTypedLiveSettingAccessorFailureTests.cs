using System.Reflection;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class PluginTypedLiveSettingAccessorFailureTests
{
    [Fact]
    public async Task Class_typed_view_wraps_throwing_live_setting_setters_with_property_context()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());

        var exception = Record.Exception(() => installed.As<ThrowingSetterSettings>());

        Assert.NotNull(exception);
        Assert.IsNotType<TargetInvocationException>(exception);
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("live setting", invalid.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(ThrowingSetterSettings.MinDamage), invalid.Message, StringComparison.Ordinal);
        var inner = Assert.IsType<InvalidOperationException>(invalid.InnerException);
        Assert.Equal("setter failed", inner.Message);
    }

    [Fact]
    public async Task Class_typed_modify_wraps_throwing_live_setting_getters_with_property_context()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var settings = server.Kernels.Get<ThrowingGetterSettings>("fire-damage");

        var exception = await Record.ExceptionAsync(async () =>
            await settings.ModifyAsync(state =>
            {
                state.MinDamage = 250;
                state.ThrowOnMinDamageGet = true;
            }).AsTask());

        Assert.NotNull(exception);
        Assert.IsNotType<TargetInvocationException>(exception);
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("live setting", invalid.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(ThrowingGetterSettings.MinDamage), invalid.Message, StringComparison.Ordinal);
        var inner = Assert.IsType<InvalidOperationException>(invalid.InnerException);
        Assert.Equal("getter failed", inner.Message);
    }

    private sealed class ThrowingSetterSettings
    {
        [LiveSetting]
        public int MinDamage
        {
            get => 100;
            set => throw new InvalidOperationException("setter failed");
        }
    }

    private sealed class ThrowingGetterSettings
    {
        private int _minDamage = 100;

        public bool ThrowOnMinDamageGet { get; set; }

        [LiveSetting]
        public int MinDamage
        {
            get
            {
                if (ThrowOnMinDamageGet)
                {
                    throw new InvalidOperationException("getter failed");
                }

                return _minDamage;
            }

            set => _minDamage = value;
        }
    }
}
