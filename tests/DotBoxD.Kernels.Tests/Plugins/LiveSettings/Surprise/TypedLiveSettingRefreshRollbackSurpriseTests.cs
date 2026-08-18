using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class TypedLiveSettingRefreshRollbackSurpriseTests
{
    [Fact]
    public async Task Failed_class_typed_refresh_does_not_commit_live_setting_values()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());
        var settings = installed.As<ThrowingRefreshSettings>();
        settings.Value.ThrowOnMinDamageSet = true;

        var exception = await Record.ExceptionAsync(async () =>
            await installed.ModifySettingsAsync(new Dictionary<string, object?>
            {
                ["MinDamage"] = 250
            }).AsTask());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(nameof(ThrowingRefreshSettings.MinDamage), invalid.Message, StringComparison.Ordinal);
        Assert.Equal("setter failed", Assert.IsType<InvalidOperationException>(invalid.InnerException).Message);
        Assert.Equal(100, installed.Value.Get<int>("MinDamage"));
    }

    private sealed class ThrowingRefreshSettings
    {
        private int _minDamage;

        public bool ThrowOnMinDamageSet { get; set; }

        [LiveSetting]
        public int MinDamage
        {
            get => _minDamage;
            set
            {
                if (ThrowOnMinDamageSet)
                {
                    throw new InvalidOperationException("setter failed");
                }

                _minDamage = value;
            }
        }
    }
}
